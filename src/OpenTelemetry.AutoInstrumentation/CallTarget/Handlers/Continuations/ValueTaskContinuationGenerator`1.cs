using System;
using System.Threading.Tasks;

#pragma warning disable SA1649 // File name must match first type name

namespace OpenTelemetry.AutoInstrumentation.CallTarget.Handlers.Continuations;
#if NETCOREAPP3_1 || NET5_0
internal class ValueTaskContinuationGenerator<TIntegration, TTarget, TReturn, TResult> : ContinuationGenerator<TTarget, TReturn>
{
    private static readonly Func<TTarget, TResult, Exception, CallTargetState, TResult> _continuation;
    private static readonly bool _preserveContext;

    static ValueTaskContinuationGenerator()
    {
        var result = IntegrationMapper.CreateAsyncEndMethodDelegate(typeof(TIntegration), typeof(TTarget), typeof(TResult));
        if (result.Method != null)
        {
            _continuation = (Func<TTarget, TResult, Exception, CallTargetState, TResult>)result.Method.CreateDelegate(typeof(Func<TTarget, TResult, Exception, CallTargetState, TResult>));
            _preserveContext = result.PreserveContext;
        }
    }

    public override TReturn SetContinuation(TTarget instance, TReturn returnValue, Exception exception, CallTargetState state)
    {
        if (_continuation is null)
        {
            return returnValue;
        }

        if (exception != null)
        {
            _continuation(instance, default, exception, state);
            return returnValue;
        }

        ValueTask<TResult> previousValueTask = FromTReturn<ValueTask<TResult>>(returnValue);
        return ToTReturn(InnerSetValueTaskContinuation(instance, previousValueTask, state));

        static async ValueTask<TResult> InnerSetValueTaskContinuation(TTarget instance, ValueTask<TResult> previousValueTask, CallTargetState state)
        {
            TResult result = default;
            try
            {
                result = await previousValueTask.ConfigureAwait(_preserveContext);
            }
            catch (Exception ex)
            {
                try
                {
                    // *
                    // Calls the CallTarget integration continuation, exceptions here should never bubble up to the application
                    // *
                    _continuation(instance, result, ex, state);
                }
                catch (Exception contEx)
                {
                    IntegrationOptions<TIntegration, TTarget>.LogException(contEx, "Exception occurred when calling the CallTarget integration continuation.");
                }

                throw;
            }

            try
            {
                // *
                // Calls the CallTarget integration continuation, exceptions here should never bubble up to the application
                // *
                return _continuation(instance, result, null, state);
            }
            catch (Exception contEx)
            {
                IntegrationOptions<TIntegration, TTarget>.LogException(contEx, "Exception occurred when calling the CallTarget integration continuation.");
            }

            return result;
        }
    }
}
#endif