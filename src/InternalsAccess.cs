// Declares the IgnoresAccessChecksTo attribute so the C# compiler lets us
// reference internal types in Assembly-CSharp at compile time.
// At runtime, Harmony's IL patching handles access independently.
using System.Runtime.CompilerServices;

[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { }
    }
}
