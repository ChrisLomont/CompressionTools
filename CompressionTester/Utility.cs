/*
MIT License

Copyright (c) 2016 Chris Lomont

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Lomont.Compression
{
    public static class Utility
    {

        /// <summary>
        /// Get all types with the given attribute type
        /// </summary>
        /// <typeparam name="TAttribute"></typeparam>
        /// <param name="inherit"></param>
        /// <returns></returns>
        public static IEnumerable<Type> GetTypesWith<TAttribute>(bool inherit) where TAttribute : Attribute
        {
            return from a in AppDomain.CurrentDomain.GetAssemblies()
                   from t in a.GetTypes()
                   where t.IsDefined(typeof(TAttribute), inherit)
                   select t;
        }

        /// <summary>
        /// Get all properties with the given attribute type
        /// </summary>
        /// <typeparam name="TAttribute"></typeparam>
        /// <returns></returns>
        public static IEnumerable<PropertyInfo> GetPropertiesWith<TAttribute>(Type type = null) where TAttribute : Attribute
        {

            var types = type == null
                ? AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                : new List<Type> {type};

            return
                types
                .SelectMany(t => t.GetProperties())
                .Where(p => p.GetCustomAttributes(typeof(TAttribute), false).Length > 0);
        }

        /// <summary>
        /// Format data as C code
        /// </summary>
        /// <param name="data"></param>
        /// <param name="itemName"></param>
        /// <param name="formatType"></param>
        public static string FormatCode(byte[] data, string itemName, string formatType, string headerMessage)
        {
            var output = new StringBuilder();
            int lineLen = 16;
            // basic C style code formatting
            output.AppendLine($"// {headerMessage}");
            output.AppendLine($"const uint8_t {itemName}[{data.Length}] = {{");
            for (var i = 0; i < data.Length; ++i)
            {
                if ((i % lineLen) == 0)
                    output.Append("   ");
                output.Append($"0x{data[i]:X2}");
                if (i != data.Length - 1)
                    output.Append(',');
                if ((i % lineLen) == lineLen - 1)
                    output.AppendLine();
            }
            output.AppendLine("};");
            return output.ToString();
        }

    }
}
