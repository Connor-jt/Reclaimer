using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Reclaimer.Blam.Halo5
{
    internal class detile
    {

        [DllImport("Dll1.dll")]
        private static extern int fnDll1(byte[] data, int data_size, short width, short height, short format, byte tilemode);

        public static void detile_bitmap(byte[] data, int data_size, short width, short height, short format, byte tilemode)
        {
            // do exception handling here???
            int result = fnDll1(data, data_size, width, height, format, tilemode);
            if (result == -1)
                throw new Exception("unknown detile error");
            else if (result != 0 && result != 5) // ignore 5??
                throw new Exception(dll_exceptions[result-1]);
        }
        private static string[] dll_exceptions = new string[]
        {
            "DXGI format specified by the tag was either unsupported or invalid",
            "image failed to generate DDS header",
            "failed to load xbox encoded image",
            "failed to detile xbox image",
            "detiled image data did not match original data length"
        };

    }
}
