using System;
using System.Collections.Generic;
using System.Reflection;
using Datadog.Trace.Ci;
using Datadog.Trace.ClrProfiler.CallTarget;
using Datadog.Trace.Configuration;
using Datadog.Trace.DuckTyping;
using Datadog.Trace.ExtensionMethods;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Testing.MsTestV2
{
    /// <summary>
    /// Microsoft.VisualStudio.TestPlatform.TestFramework.Execute calltarget instrumentation
    /// </summary>
    [InstrumentMethod(
        AssemblyName = "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter",
        TypeName = "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodRunner",
        MethodName = "Execute",
        ReturnTypeName = "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.ObjectModel.UnitTestResult",
        ParameterTypeNames = new string[0],
        MinimumVersion = "14.0.0",
        MaximumVersion = "14.*.*",
        IntegrationName = IntegrationName)]
    public class TestMethodRunnerExecuteIntegration
    {
        private const string IntegrationName = nameof(IntegrationIds.MsTestV2);
        private static readonly IntegrationInfo IntegrationId = IntegrationRegistry.GetIntegrationInfo(IntegrationName);

        /// <summary>
        /// OnMethodBegin callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <returns>Calltarget state value</returns>
        public static CallTargetState OnMethodBegin<TTarget>(TTarget instance)
            where TTarget : ITestMethodRunner, IDuckType
        {
            if (!Common.TestTracer.Settings.IsIntegrationEnabled(IntegrationId))
            {
                return CallTargetState.GetDefault();
            }

            ITestMethod testMethodInfo = instance.TestMethodInfo;
            MethodInfo testMethod = testMethodInfo.MethodInfo;
            object[] testMethodArguments = testMethodInfo.Arguments;

            string testFramework = "MSTestV2 " + instance.Type.Assembly.GetName().Version;
            string testSuite = testMethodInfo.TestClassName;
            string testName = testMethodInfo.TestMethodName;

            Scope scope = Common.TestTracer.StartActive("mstest.test", serviceName: Common.ServiceName);
            Span span = scope.Span;

            span.Type = SpanTypes.Test;
            span.SetTraceSamplingPriority(SamplingPriority.AutoKeep);
            span.ResourceName = $"{testSuite}.{testName}";
            span.SetTag(TestTags.Suite, testSuite);
            span.SetTag(TestTags.Name, testName);
            span.SetTag(TestTags.Framework, testFramework);
            span.SetTag(TestTags.Type, TestTags.TypeTest);
            CIEnvironmentValues.DecorateSpan(span);

            var framework = FrameworkDescription.Instance;

            span.SetTag(CommonTags.RuntimeName, framework.Name);
            span.SetTag(CommonTags.RuntimeVersion, framework.ProductVersion);
            span.SetTag(CommonTags.RuntimeArchitecture, framework.ProcessArchitecture);
            span.SetTag(CommonTags.OSArchitecture, framework.OSArchitecture);
            span.SetTag(CommonTags.OSPlatform, framework.OSPlatform);
            span.SetTag(CommonTags.OSVersion, Environment.OSVersion.VersionString);

            // Get test parameters
            ParameterInfo[] methodParameters = testMethod.GetParameters();
            if (methodParameters?.Length > 0)
            {
                TestParameters testParameters = new TestParameters();
                testParameters.Metadata = new Dictionary<string, object>();
                testParameters.Arguments = new Dictionary<string, object>();

                for (int i = 0; i < methodParameters.Length; i++)
                {
                    if (testMethodArguments != null && i < testMethodArguments.Length)
                    {
                        testParameters.Arguments[methodParameters[i].Name] = testMethodArguments[i]?.ToString() ?? "(null)";
                    }
                    else
                    {
                        testParameters.Arguments[methodParameters[i].Name] = "(default)";
                    }
                }

                span.SetTag(TestTags.Parameters, testParameters.ToJSON());
            }

            // Get traits
            Dictionary<string, List<string>> testTraits = GetTraits(testMethod);
            if (testTraits != null && testTraits.Count > 0)
            {
                span.SetTag(TestTags.Traits, Datadog.Trace.Vendors.Newtonsoft.Json.JsonConvert.SerializeObject(testTraits));
            }

            span.ResetStartTime();
            return new CallTargetState(scope);
        }

        /// <summary>
        /// OnAsyncMethodEnd callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <typeparam name="TReturn">Type of the return value</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="returnValue">Return value</param>
        /// <param name="exception">Exception instance in case the original code threw an exception.</param>
        /// <param name="state">Calltarget state value</param>
        /// <returns>A response value, in an async scenario will be T of Task of T</returns>
        public static CallTargetReturn<TReturn> OnMethodEnd<TTarget, TReturn>(TTarget instance, TReturn returnValue, Exception exception, CallTargetState state)
            where TTarget : ITestMethodRunner
        {
            Scope scope = state.Scope;
            if (scope != null)
            {
                Array returnValueArray = returnValue as Array;
                if (returnValueArray.Length == 1)
                {
                    object unitTestResultObject = returnValueArray.GetValue(0);
                    if (unitTestResultObject != null && unitTestResultObject.TryDuckCast<UnitTestResultStruct>(out var unitTestResult))
                    {
                        switch (unitTestResult.Outcome)
                        {
                            case UnitTestResultOutcome.Error:
                            case UnitTestResultOutcome.Failed:
                            case UnitTestResultOutcome.NotFound:
                            case UnitTestResultOutcome.Timeout:
                                scope.Span.SetTag(TestTags.Status, TestTags.StatusFail);
                                scope.Span.Error = true;
                                scope.Span.SetTag(Tags.ErrorMsg, unitTestResult.ErrorMessage);
                                scope.Span.SetTag(Tags.ErrorStack, unitTestResult.ErrorStackTrace);
                                break;
                            case UnitTestResultOutcome.Inconclusive:
                            case UnitTestResultOutcome.NotRunnable:
                            case UnitTestResultOutcome.Ignored:
                                scope.Span.SetTag(TestTags.Status, TestTags.StatusSkip);
                                scope.Span.SetTag(TestTags.SkipReason, unitTestResult.ErrorMessage);
                                break;
                            case UnitTestResultOutcome.Passed:
                                scope.Span.SetTag(TestTags.Status, TestTags.StatusPass);
                                break;
                        }
                    }
                }

                scope.Dispose();
            }

            return new CallTargetReturn<TReturn>(returnValue);
        }

        private static Dictionary<string, List<string>> GetTraits(MethodInfo methodInfo)
        {
            Dictionary<string, List<string>> testProperties = null;
            var testAttributes = methodInfo.GetCustomAttributes(true);
            foreach (var tattr in testAttributes)
            {
                if (tattr?.GetType().Name != "TestCategoryAttribute")
                {
                    continue;
                }

                testProperties ??= new Dictionary<string, List<string>>();
                if (!testProperties.TryGetValue("Category", out var categoryList))
                {
                    categoryList = new List<string>();
                    testProperties["Category"] = categoryList;
                }

                if (tattr.TryDuckCast<TestCategoryAttributeStruct>(out var tattrStruct))
                {
                    categoryList.AddRange(tattrStruct.TestCategories);
                }
            }

            var classCategories = methodInfo.DeclaringType?.GetCustomAttributes(true);
            if (classCategories is not null)
            {
                foreach (var tattr in classCategories)
                {
                    if (tattr.GetType().Name != "TestCategoryAttribute")
                    {
                        continue;
                    }

                    testProperties ??= new Dictionary<string, List<string>>();
                    if (!testProperties.TryGetValue("Category", out var categoryList))
                    {
                        categoryList = new List<string>();
                        testProperties["Category"] = categoryList;
                    }

                    if (tattr.TryDuckCast<TestCategoryAttributeStruct>(out var tattrStruct))
                    {
                        categoryList.AddRange(tattrStruct.TestCategories);
                    }
                }
            }

            return testProperties;
        }
    }
}
