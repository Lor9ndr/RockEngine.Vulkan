using System.Runtime.InteropServices;
using System.Text;

namespace RockEngine.Vulkan.Helpers
{
    internal static class UnmanagedExtensions
    {
        /// <summary>
        /// Adds a new string to an existing unmanaged byte** array.
        /// </summary>
        /// <param name="originalArray">The original byte** array.</param>
        /// <param name="originalLength">The length of the original array.</param>
        /// <param name="newString">The new string to add.</param>
        /// <returns>A new byte** array containing the original data and the new string.</returns>
        public static unsafe byte** AddToStringArray(byte** originalArray, uint originalLength, string newString, Encoding encoding)
        {
            // Allocate unmanaged memory for the new array, which is one element larger.
            byte** newArray = (byte**)Marshal.AllocHGlobal(sizeof(byte*) * (int)(originalLength + 1));

            // Copy the original pointers to the new array.
            for (int i = 0; i < originalLength; i++)
            {
                newArray[i] = originalArray[i];
            }

            // Convert the new string to a null-terminated encoded byte array.
            byte[] newStringBytes = encoding.GetBytes(newString + "\0");
            newArray[originalLength] = (byte*)Marshal.AllocHGlobal(newStringBytes.Length);
            Marshal.Copy(newStringBytes, 0, (nint)newArray[originalLength], newStringBytes.Length);

            // Return the new array. Remember to free the original array if it's no longer needed.
            return newArray;
        }

        /// <summary>
        /// Converts an array of strings to an unmanaged array of pointers to null-terminated UTF-8 encoded byte arrays.
        /// </summary>
        /// <param name="array">The array of strings to convert.</param>
        /// <returns>A pointer to the first element of an array of pointers to byte arrays.</returns>
        public static unsafe byte** ToUnmanagedArray(this string[] array, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(array);

            // Allocate unmanaged memory for the array of pointers. Each pointer will point to a null-terminated UTF-8 encoded byte array.
            byte** unmanagedArray = (byte**)Marshal.AllocHGlobal(array.Length * sizeof(byte*));

            for (int i = 0; i < array.Length; i++)
            {
                // Convert each string to a null-terminated UTF-8 encoded byte array.
                byte[] bytes = encoding.GetBytes(array[i] + "\0");

                // Allocate unmanaged memory for the byte array and copy the data.
                unmanagedArray[i] = (byte*)Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, (nint)unmanagedArray[i], bytes.Length);
            }

            return unmanagedArray;
        }

        /// <summary>
        /// Frees the unmanaged memory allocated by ToUnmanagedUtf8Array.
        /// </summary>
        /// <param name="unmanagedArray">The unmanaged array to free.</param>
        /// <param name="length">The length of the array.</param>
        public static unsafe void FreeUnmanagedArray(byte** unmanagedArray, int length)
        {
            ArgumentNullException.ThrowIfNull(unmanagedArray);

            for (int i = 0; i < length; i++)
            {
                // Free the unmanaged memory allocated for each byte array.
                Marshal.FreeHGlobal((nint)unmanagedArray[i]);
            }

            // Free the unmanaged memory allocated for the array of pointers.
            Marshal.FreeHGlobal((nint)unmanagedArray);
        }
    }
}
