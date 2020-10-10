using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace update_package_reference
{
    class Program
    {


        static int Main(string[] args)
        {
            reference_update updater = new reference_update();
            return updater.Run(args);
        }
    }
}
