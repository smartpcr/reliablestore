using System;
using Microsoft.Owin.Hosting;

namespace ShippingService
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WebApp.Start<Startup>("http://*:9005"))
            {
                Console.WriteLine("ShippingService running...");
                Console.ReadLine();
            }
        }
    }
}
