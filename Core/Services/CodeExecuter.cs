using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Compilify.Services
{
    public class CodeExecuter
    {
        private static readonly string[] Namespaces =
            new[]
            {
                "System", 
                "System.IO", 
                "System.Net", 
                "System.Linq", 
                "System.Text", 
                "System.Text.RegularExpressions", 
                "System.Collections.Generic"
            };

        public const string EntryPoint = @"public class EntryPoint 
                                           {
                                               public static object Result { get; set; }
                      
                                               public static void Main()
                                               {
                                                   Result = Script.Eval();
                                               }
                                           }";

        public object Execute(string code)
        {
            if (!Validator.Validate(code))
            {
                return "Not supported";
            } 

            var sandbox = SecureAppDomainFactory.Create();

            // Load basic .NET assemblies into our sandbox
            var mscorlib = sandbox.Load("mscorlib,Version=4.0.0.0,Culture=neutral,PublicKeyToken=b77a5c561934e089");
            var system = sandbox.Load("System,Version=4.0.0.0,Culture=neutral,PublicKeyToken=b77a5c561934e089");
            var core = sandbox.Load("System.Core,Version=4.0.0.0,Culture=neutral,PublicKeyToken=b77a5c561934e089");

            var script = "public static object Eval() {" + code + "}";
            
            var options = new CompilationOptions(assemblyKind: AssemblyKind.ConsoleApplication, usings: ReadOnlyArray<string>.CreateFrom(Namespaces));

            var compilation = Compilation.Create("foo", options,
                new[]
                {
                    SyntaxTree.ParseCompilationUnit(EntryPoint),
                    // This is the syntax tree represented in the `Script` variable.
                    SyntaxTree.ParseCompilationUnit(script, options: new ParseOptions(kind: SourceCodeKind.Interactive))
                },
                new MetadataReference[] { 
                    new AssemblyFileReference(core.Location), 
                    new AssemblyFileReference(system.Location),
                    new AssemblyFileReference(mscorlib.Location)
                });

            byte[] compiledAssembly;
            using (var output = new MemoryStream())
            {
                var emitResult = compilation.Emit(output);

                if (!emitResult.Success)
                {
                    var errors = emitResult.Diagnostics.Select(x => x.Info.GetMessage().Replace("Eval()", "<Factory>()")).ToArray();
                    return string.Join(", ", errors);
                }

                compiledAssembly = output.ToArray();
            }

            if (compiledAssembly.Length == 0)
            {
                return "Incorrect data";
            }

            var loader = (ByteCodeLoader)Activator.CreateInstance(sandbox, typeof(ByteCodeLoader).Assembly.FullName, typeof(ByteCodeLoader).FullName).Unwrap();

            bool unloaded = false;
            object result = null;
            var timeout = TimeSpan.FromSeconds(5);
            try
            {
                var task = Task.Factory.StartNew(() =>
                                                 {
                                                     try
                                                     {
                                                         result = loader.Run(compiledAssembly);
                                                     }
                                                     catch (Exception ex)
                                                     {
                                                         result = ex;
                                                     }
                                                 }, TaskCreationOptions.PreferFairness);

                if (!task.Wait(timeout))
                {
                    AppDomain.Unload(sandbox);
                    unloaded = true;
                    result = "[Execution timed out after 5 seconds]";
                }
            }
            catch (Exception ex)
            {
                result = ex;
            }
            
            if (!unloaded)
            {
                AppDomain.Unload(sandbox);
            }
            
            if (result == null || string.IsNullOrEmpty(result.ToString()))
            {
                result = "null";
            }

            return result;
        }
    }
}