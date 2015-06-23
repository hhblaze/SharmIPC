using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tiesky.com.SharmNet
{
    internal static class BytesProcessing
    {
        /// <summary>
        /// Substring int-dimensional byte arrays
        /// </summary>
        /// <param name="ar"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static byte[] Substring(this byte[] ar, int startIndex, int length)
        {            

            if (ar == null)
                return null;

            if (ar.Length < 1)
                return ar;

            if (startIndex > ar.Length - 1)
                return null;

            if (startIndex + length > ar.Length)
            {
                //we make length till the end of array
                length = ar.Length - startIndex;
            }

            byte[] ret = new byte[length];

            Buffer.BlockCopy(ar, startIndex, ret, 0, length);

            return ret;
        }

        /// <summary>
        /// From 4 bytes array which is in BigEndian order (highest byte first, lowest last) makes uint.
        /// If array not equal 4 bytes throws exception. (0 to 4.294.967.295)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint To_UInt32_BigEndian(this byte[] value)
        {
            return (uint)(value[0] << 24 | value[1] << 16 | value[2] << 8 | value[3]);
        }

        /// <summary>
        /// From Int32 to 4 bytes array with BigEndian order (highest byte first, lowest last).        
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] To_4_bytes_array_BigEndian(this int value)
        {          
            uint val1 = (uint)(value - int.MinValue);

            return new byte[] 
            { 
                (byte)(val1 >> 24), 
                (byte)(val1 >> 16), 
                (byte)(val1 >> 8), 
                (byte) val1 
            };
       
        }


    }
}
