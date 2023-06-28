// <copyright file="InstrumentationDefinitions.cs" company="OpenTelemetry Authors">
// Copyright The OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

namespace OpenTelemetry.AutoInstrumentation;

internal static partial class InstrumentationDefinitions
{
    internal static Payload GetAllDefinitions()
    {
        return new Payload
        {
            // Fixed Id for definitions payload (to avoid loading same integrations from multiple AppDomains)
            DefinitionsId = "FFAFA5168C4F4718B40CA8788875C2DA",

            // Autogenerated definitions array
            Definitions = GetDefinitionsArray(),
        };
    }

    internal static Payload GetDerivedDefinitions()
    {
        return new Payload
        {
            // Fixed Id for definitions payload (to avoid loading same integrations from multiple AppDomains)
            DefinitionsId = "61BF627FA9B5477F85595A9F0D68B29C",

            // Autogenerated definitions array
            Definitions = GetDerivedDefinitionsArray(),
        };
    }

    // TODO: Generate this list using source generators
    private static NativeCallTargetDefinition[] GetDerivedDefinitionsArray()
        => new NativeCallTargetDefinition[]
        {
        };

    internal struct Payload
    {
        public string DefinitionsId { get; set; }

        public NativeCallTargetDefinition[] Definitions { get; set; }
    }
}
