using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockEngine.Core.Attributes
{
    /// <summary>
    /// Marks a property to be ignored during serialization (both JSON and binary)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class SerializeIgnoreAttribute : Attribute { }

}

