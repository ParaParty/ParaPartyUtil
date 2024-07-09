using System.Linq;
using System.Text;

namespace Paraparty.UnityNative
{
    /// <summary>
    /// Unity Native String Extension
    /// </summary>
    public static class StringExtension
    {
        /// <summary>
        /// Return the UTF-8 bytes of a string with '\0' ends. 
        /// </summary>
        /// <param name="self"></param>
        /// <returns></returns>
        public static byte[] CStr(this string self)
        {
            return Encoding.UTF8.GetBytes(self).Append<byte>(0).ToArray();
        }
    }
}
