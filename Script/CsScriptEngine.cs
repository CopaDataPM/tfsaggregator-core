﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text;

namespace Aggregator.Core
{
    /// <summary>
    /// Compiles C# code on the fly as if scripting engine
    /// </summary>
    public class CsScriptEngine : ScriptEngine
    {
        public CsScriptEngine(string scriptName, string script, IWorkItemRepository store, ILogEvents logger)
            : base(scriptName, script, store, logger)
        {
        }

        public interface IScript
        {
            object RunScript(IWorkItem self, IWorkItem parent);
        }

        private string WrapScript(string script)
        {
            return @"
namespace DO_NOT_CLASH
{
  public class Script_" + this.scriptName + @" : Aggregator.Core.CsScriptEngine.IScript
  {
    public object RunScript(Aggregator.Core.IWorkItem self, Aggregator.Core.IWorkItem parent)
    {
"+ script + @"
    }
  }
}
";
        }

        private Assembly CompileCode(string code)
        {
            var csharpProvider = new Microsoft.CSharp.CSharpCodeProvider();

            // Setup our options
            var compilerOptions = new CompilerParameters();
            compilerOptions.GenerateExecutable = false;
            compilerOptions.GenerateInMemory = true;

            // CAREFUL HERE
            compilerOptions.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);

            CompilerResults compilerResult;
            compilerResult = csharpProvider.CompileAssemblyFromSource(compilerOptions, code);

            if (compilerResult.Errors.HasErrors)
            {
                foreach (CompilerError err in compilerResult.Errors)
                {
                    logger.ScriptHasError(this.scriptName, err.Line - 8, err.Column, err.ErrorNumber, err.ErrorText);
                }
                return null;
            }

            if (compilerResult.Errors.HasWarnings)
            {
                foreach (CompilerError err in compilerResult.Errors)
                {
                    //TODO warning instead of errors
                    logger.ScriptHasError(this.scriptName, err.Line - 8, err.Column, err.ErrorNumber, err.ErrorText);
                }
            }

            return compilerResult.CompiledAssembly;
        }

        private void RunScript(Assembly assembly, IWorkItem self, IWorkItem parent)
        {
            // Now that we have a compiled script, lets run them
            foreach (Type type in assembly.GetExportedTypes())
            {
                foreach (Type iface in type.GetInterfaces())
                {
                    if (iface == typeof(IScript))
                    {
                        ConstructorInfo constructor = type.GetConstructor(System.Type.EmptyTypes);
                        if (constructor != null && constructor.IsPublic)
                        {
                            // we specified that we wanted a constructor that doesn't take parameters, so don't pass parameters
                            IScript scriptObject = constructor.Invoke(null) as IScript;
                            if (scriptObject != null)
                            {
                                //Lets run our script and display its results
                                object result = scriptObject.RunScript(self, parent);
                                logger.ResultsFromScriptRun(this.scriptName, result);
                            }
                            else
                            {
                                // hmmm, for some reason it didn't create the object
                                // this shouldn't happen, as we have been doing checks all along, but we should
                                // inform the user something bad has happened, and possibly request them to send
                                // you the script so you can debug this problem
                            }
                        }
                        else
                        {
                            // and even more friendly and explain that there was no valid constructor
                            // found and that's why this script object wasn't run
                        }
                    }
                }
            }
        }

        override public void Run(IWorkItem workItem)
        {
            // TODO we can build a single assembly and class from multiple scripts
            // a method for each script
            string code = WrapScript(this.script);
            var asm = CompileCode(code);
            if (asm != null)
            {
                RunScript(asm, workItem, workItem.Parent);
            }
        }
    }
}
