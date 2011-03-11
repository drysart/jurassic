﻿using System;

namespace Jurassic.Compiler
{
    /// <summary>
    /// Represents the unit of compilation.
    /// </summary>
    internal abstract class MethodGenerator
    {
        /// <summary>
        /// Creates a new MethodGenerator instance.
        /// </summary>
        /// <param name="engine"> The script engine. </param>
        /// <param name="scope"> The initial scope. </param>
        /// <param name="source"> The source of javascript code. </param>
        /// <param name="options"> Options that influence the compiler. </param>
        protected MethodGenerator(ScriptEngine engine, Scope scope, ScriptSource source, CompilerOptions options)
        {
            if (engine == null)
                throw new ArgumentNullException("engine");
            if (scope == null)
                throw new ArgumentNullException("scope");
            if (source == null)
                throw new ArgumentNullException("source");
            if (options == null)
                throw new ArgumentNullException("options");
            this.Engine = engine;
            this.InitialScope = scope;
            this.Source = source;
            this.Options = options;
            this.StrictMode = this.Options.ForceStrictMode;
        }

        /// <summary>
        /// Gets a reference to the script engine.
        /// </summary>
        public ScriptEngine Engine
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a reference to any compiler options.
        /// </summary>
        public CompilerOptions Options
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a value that indicates whether strict mode is enabled.
        /// </summary>
        public bool StrictMode
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the top-level scope associated with the context.
        /// </summary>
        public Scope InitialScope
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source of javascript code.
        /// </summary>
        public ScriptSource Source
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the root node of the abstract syntax tree.  This will be <c>null</c> until Parse()
        /// is called.
        /// </summary>
        public Statement AbstractSyntaxTree
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets the emitted method.  This will be <c>null</c> until GenerateCode() is called.
        /// </summary>
        public System.Reflection.MethodInfo GeneratedMethod
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets the generated IL.  This will be <c>null</c> until GenerateCode() is
        /// called.
        /// </summary>
        public ILGenerator ILGenerator
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets a delegate to the emitted dynamic method.  This will be <c>null</c> until
        /// Execute() is called.
        /// </summary>
        public Delegate CompiledDelegate
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets a name for the generated method.
        /// </summary>
        /// <returns> A name for the generated method. </returns>
        protected abstract string GetMethodName();

        /// <summary>
        /// Gets an array of types - one for each parameter accepted by the method generated by
        /// this context.
        /// </summary>
        /// <returns> An array of parameter types. </returns>
        protected virtual Type[] GetParameterTypes()
        {
            return new Type[] {
                typeof(ScriptEngine),   // The script engine.
                typeof(Scope),          // The scope.
                typeof(object),         // The "this" object.
            };
        }

        /// <summary>
        /// Parses the source text into an abstract syntax tree.
        /// </summary>
        public virtual void Parse()
        {
            var lexer = new Lexer(this.Engine, this.Source);
            var parser = new Parser(this.Engine, lexer, this.InitialScope, this is FunctionMethodGenerator, this.Options);
            this.AbstractSyntaxTree = parser.Parse();
            this.StrictMode = parser.StrictMode;
        }

        /// <summary>
        /// Optimizes the abstract syntax tree.
        /// </summary>
        public void Optimize()
        {
            // Generate the abstract syntax tree if it hasn't already been generated.
            //if (this.AbstractSyntaxTree == null)
            //    Parse();
            //this.AbstractSyntaxTree.Optimize();
        }

        /// <summary>
        /// Generates IL for the script.
        /// </summary>
        public void GenerateCode()
        {
            // Generate the abstract syntax tree if it hasn't already been generated.
            if (this.AbstractSyntaxTree == null)
            {
                Parse();
                Optimize();
            }

            if (this.Options.EnableDebugging == false)
            {
                // Create a new dynamic method.
                var dynamicMethod = new System.Reflection.Emit.DynamicMethod(
                    "Main",                                                 // Name of the generated method.
                    typeof(object),                                         // Return type of the generated method.
                    GetParameterTypes(),                                    // Parameter types of the generated method.
                    typeof(MethodGenerator),                                // Owner type.
                    true);                                                  // Skip visibility checks.

                // Generate the IL.
                var generator = new DynamicILGenerator(dynamicMethod);
                GenerateCode(generator, new OptimizationInfo(this.Engine));
                generator.Complete();

                // Create a delegate from the method.
                this.GeneratedMethod = dynamicMethod;
                this.CompiledDelegate = dynamicMethod.CreateDelegate(GetDelegate());
            }
            else
            {
                var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "JurassicDebug.dll");

                ScriptEngine.ReflectionEmitModuleInfo reflectionEmitInfo = this.Engine.ReflectionEmitInfo;
                if (reflectionEmitInfo == null)
                {
                    reflectionEmitInfo = new ScriptEngine.ReflectionEmitModuleInfo();

                    // Create a dynamic assembly and module.
                    reflectionEmitInfo.AssemblyBuilder = System.Threading.Thread.GetDomain().DefineDynamicAssembly(new System.Reflection.AssemblyName("Debug"),
                        System.Reflection.Emit.AssemblyBuilderAccess.RunAndSave, System.IO.Path.GetDirectoryName(filePath), false, null);

                    // Mark the assembly as debuggable.  This must be done before the module is created.
                    var debuggableAttributeConstructor = typeof(System.Diagnostics.DebuggableAttribute).GetConstructor(
                        new Type[] { typeof(System.Diagnostics.DebuggableAttribute.DebuggingModes) });
                    reflectionEmitInfo.AssemblyBuilder.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(debuggableAttributeConstructor, new object[] { 
                System.Diagnostics.DebuggableAttribute.DebuggingModes.DisableOptimizations | 
                System.Diagnostics.DebuggableAttribute.DebuggingModes.Default }));

                    // Create a dynamic module.
                    reflectionEmitInfo.ModuleBuilder = reflectionEmitInfo.AssemblyBuilder.DefineDynamicModule("Module", System.IO.Path.GetFileName(filePath), true);

                    this.Engine.ReflectionEmitInfo = reflectionEmitInfo;
                }

                // Create a new type to hold our method.
                var typeBuilder = reflectionEmitInfo.ModuleBuilder.DefineType("JavaScriptClass" + reflectionEmitInfo.TypeCount.ToString(), System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
                reflectionEmitInfo.TypeCount++;

                // Create a method.
                var methodBuilder = typeBuilder.DefineMethod(this.GetMethodName(),
                    System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Static | System.Reflection.MethodAttributes.Public,
                    typeof(object), GetParameterTypes());

                // Generate the IL for the method.
                var generator = new ReflectionEmitILGenerator(methodBuilder);
                var optimizationInfo = new OptimizationInfo(this.Engine);
                optimizationInfo.StrictMode = this.StrictMode;
                if (this.Source.Path != null)
                    optimizationInfo.DebugDocument = reflectionEmitInfo.ModuleBuilder.DefineDocument(this.Source.Path, COMHelpers.LanguageType, COMHelpers.LanguageVendor, COMHelpers.DocumentType);
                GenerateCode(generator, optimizationInfo);
                generator.Complete();

                // Bake it.
                var type = typeBuilder.CreateType();
                this.GeneratedMethod = type.GetMethod(this.GetMethodName());

                //// Disassemble the IL.
                //var reader = new ClrTest.Reflection.ILReader(this.GeneratedMethod);
                //var writer = new System.IO.StringWriter();
                //var visitor = new ClrTest.Reflection.ReadableILStringVisitor(new ClrTest.Reflection.ReadableILStringToTextWriter(writer));
                //reader.Accept(visitor);
                //string il = writer.ToString();

                //// set the entry point for the application and save it
                //assemblyBuilder.Save(System.IO.Path.GetFileName(filePath));

                //// Copy this DLL there as well.
                //var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                //System.IO.File.Copy(assemblyPath, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath), System.IO.Path.GetFileName(assemblyPath)), true);

                //var startInfo = new System.Diagnostics.ProcessStartInfo();
                //startInfo.FileName = @"C:\Program Files (x86)\Microsoft SDKs\Windows\v7.0A\Bin\NETFX 4.0 Tools\x64\PEVerify.exe";
                //startInfo.Arguments = string.Format("\"{0}\" /nologo /verbose /unique", filePath);
                //startInfo.CreateNoWindow = true;
                //startInfo.RedirectStandardOutput = true;
                //startInfo.UseShellExecute = false;
                //var verifyProcess = System.Diagnostics.Process.Start(startInfo);
                //string output = verifyProcess.StandardOutput.ReadToEnd();

                //if (verifyProcess.ExitCode != 0)
                //{
                //    throw new InvalidOperationException(output);
                //}
                //else
                //{
                //    //System.Diagnostics.Process.Start(@"C:\Program Files\Reflector\Reflector.exe", string.Format("\"{0}\" /select:JavaScriptClass", filePath));
                //    //Environment.Exit(0);
                //}

                this.CompiledDelegate = Delegate.CreateDelegate(GetDelegate(), this.GeneratedMethod);
            }
        }

        /// <summary>
        /// Generates IL for the script.
        /// </summary>
        /// <param name="generator"> The generator to output the CIL to. </param>
        /// <param name="optimizationInfo"> Information about any optimizations that should be performed. </param>
        protected abstract void GenerateCode(ILGenerator generator, OptimizationInfo optimizationInfo);

        /// <summary>
        /// Retrieves a delegate for the generated method.
        /// </summary>
        /// <param name="types"> The parameter types. </param>
        /// <returns> The delegate type that matches the method parameters. </returns>
        protected virtual Type GetDelegate()
        {
            var types = GetParameterTypes();
            Array.Resize(ref types, types.Length + 1);
            types[types.Length - 1] = typeof(object);
            switch (types.Length)
            {
                case 1:
                    return typeof(Func<>).MakeGenericType(types);

                case 2:
                    return typeof(Func<,>).MakeGenericType(types);

                case 3:
                    return typeof(Func<,,>).MakeGenericType(types);

                case 4:
                    return typeof(Func<,,,>).MakeGenericType(types);

                case 5:
                    return typeof(Func<,,,,>).MakeGenericType(types);

                case 6:
                    return typeof(Func<,,,,,>).MakeGenericType(types);

                case 7:
                    return typeof(Func<,,,,,,>).MakeGenericType(types);

                case 8:
                    return typeof(Func<,,,,,,,>).MakeGenericType(types);

                case 9:
                    return typeof(Func<,,,,,,,,>).MakeGenericType(types);

                case 10:
                    return typeof(Func<,,,,,,,,,>).MakeGenericType(types);

                case 11:
                    return typeof(Func<,,,,,,,,,,>).MakeGenericType(types);

                case 12:
                    return typeof(Func<,,,,,,,,,,,>).MakeGenericType(types);

                case 13:
                    return typeof(Func<,,,,,,,,,,,,>).MakeGenericType(types);

                case 14:
                    return typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(types);

                case 15:
                    return typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(types);

                case 16:
                    return typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(types);

                case 17:
                    return typeof(Func<,,,,,,,,,,,,,,,,>).MakeGenericType(types);
            }
            throw new InvalidOperationException("Too many arguments.");
        }
    }

}