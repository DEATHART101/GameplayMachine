using System;
using System.Collections.Generic;
using System.Text;

namespace TestCommon
{
    public class ConsoleLogger : XLogger.Logger
    {
        protected override void OnLog(string builtContent)
        {
            Console.WriteLine(builtContent);
        }
    }
}
