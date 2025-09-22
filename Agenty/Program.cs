// File: Program.cs
using Agenty.Test;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Agenty
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            //await PlanningRunner.RunAsync();
            //await RAGToolCallingRunner.RunAsync();
            await RAGRunner.RunAsync();
            //await ToolCallingRunner.RunAsync();
        }
    }
}
