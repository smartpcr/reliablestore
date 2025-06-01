using System;
using Microsoft.Owin.Hosting;

namespace PaymentService
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WebApp.Start<Startup>("http://*:9004"))
            {
                Console.WriteLine("PaymentService running...");
                Console.ReadLine();
            }
        }
    }
}
