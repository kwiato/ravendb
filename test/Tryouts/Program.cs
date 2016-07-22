using System;
using System.Diagnostics;
using Raven.Client.Document;
using Raven.Client.Smuggler;
using Sparrow.Json;

namespace Tryouts
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var pool = new UnmanagedBuffersPool("test");
        }
    }
}