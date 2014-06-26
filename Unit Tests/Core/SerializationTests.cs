using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Jurassic;
using Jurassic.Library;

namespace UnitTests
{
    /// <summary>
    /// Test .NET serialization.
    /// </summary>
    [TestClass]
    public class SerializationTests
    {
        [TestMethod]
        public void SerializeEngine()
        {
            // Set up a script engine.
            var scriptEngine = new ScriptEngine();
            scriptEngine.SetGlobalValue("test", "one");

            // Attempt to serialize and then deserialize the entire script engine.
            var scriptEngine2 = (ScriptEngine)Clone(scriptEngine);

            // Verify it was deserialized correctly.
            Assert.AreEqual("one", scriptEngine2.GetGlobalValue<string>("test"));
            Assert.AreEqual(scriptEngine2, scriptEngine2.Global.Engine);
        }

        [TestMethod]
        public void SerializeObject()
        {
            var scriptEngine = new ScriptEngine();
            ScriptEngine.DeserializationEnvironment = scriptEngine;

            // Create a test object.
            scriptEngine.Execute(@"
                _obj = { };
                Object.defineProperty(_obj, 'a', { configurable: true, writable: true, enumerable: true, value: 5 });
                Object.defineProperty(_obj, 'b', { configurable: false, writable: false, enumerable: false, value: 10 });
                Object.defineProperty(_obj, 'c', { configurable: true, enumerable: true, get: function() { return 3; } });
                Object.defineProperty(_obj, 'd', { configurable: true, enumerable: true, get: function() { return this._value; }, set: function(value) { this._value = value + 1 } });
                _obj.nested = { a: 1 };");

            // Clone the object using serialization.
            scriptEngine.SetGlobalValue("_obj2", Clone(scriptEngine.GetGlobalValue("_obj")));
            
            // Check the cloned object is not simply a pointer to the old object.
            Assert.AreEqual(true, scriptEngine.Evaluate("delete _obj.e; _obj2.e = 11; _obj.e === undefined"));

            // Check the properties have been cloned successfully.
            Assert.AreEqual(5, scriptEngine.Evaluate("_obj2.a"));
            scriptEngine.Execute("var descriptor = Object.getOwnPropertyDescriptor(_obj2, 'a')");
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.configurable"));
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.writable"));
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.enumerable"));
            Assert.AreEqual(5, scriptEngine.Evaluate("descriptor.value"));

            // Check the properties have been cloned successfully.
            Assert.AreEqual(10, scriptEngine.Evaluate("_obj2.b"));
            scriptEngine.Execute("var descriptor = Object.getOwnPropertyDescriptor(_obj2, 'b')");
            Assert.AreEqual(false, scriptEngine.Evaluate("descriptor.configurable"));
            Assert.AreEqual(false, scriptEngine.Evaluate("descriptor.writable"));
            Assert.AreEqual(false, scriptEngine.Evaluate("descriptor.enumerable"));
            Assert.AreEqual(10, scriptEngine.Evaluate("descriptor.value"));

            // Check the properties have been cloned successfully.
            Assert.AreEqual(3, scriptEngine.Evaluate("_obj2.c"));
            scriptEngine.Execute("var descriptor = Object.getOwnPropertyDescriptor(_obj2, 'c')");
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.configurable"));
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.enumerable"));
            Assert.AreEqual("function", scriptEngine.Evaluate("typeof descriptor.get"));

            // Check the properties have been cloned successfully.
            Assert.AreEqual(11, scriptEngine.Evaluate("_obj2.d = 10; _obj2.d"));
            scriptEngine.Execute("var descriptor = Object.getOwnPropertyDescriptor(_obj2, 'd')");
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.configurable"));
            Assert.AreEqual(true, scriptEngine.Evaluate("descriptor.enumerable"));
            Assert.AreEqual("function", scriptEngine.Evaluate("typeof descriptor.get"));
            Assert.AreEqual("function", scriptEngine.Evaluate("typeof descriptor.set"));

            // Check the properties have been cloned successfully.
            Assert.AreEqual(1, scriptEngine.Evaluate("_obj2.nested.a"));

            // Make sure the extensible flag works.
            scriptEngine.Execute(@"
                _obj3 = { };
                Object.preventExtensions(_obj3);");

            // Clone the object using serialization.
            scriptEngine.SetGlobalValue("_obj4", Clone(scriptEngine.GetGlobalValue("_obj3")));

            // Check the flag was cloned successfully.
            Assert.AreEqual(true, scriptEngine.Evaluate("Object.isExtensible(_obj)"));
            Assert.AreEqual(true, scriptEngine.Evaluate("Object.isExtensible(_obj2)"));
            Assert.AreEqual(false, scriptEngine.Evaluate("Object.isExtensible(_obj3)"));
            Assert.AreEqual(false, scriptEngine.Evaluate("Object.isExtensible(_obj4)"));
        }

        [TestMethod]
        public void SerializeFunction()
        {
            // Set up a script engine.
            var scriptEngine = new ScriptEngine();
            scriptEngine.Execute(@"function outer(a, b) { function inner() { return a + b; } return inner(); }");

            // Attempt to serialize and then deserialize the function.
            ScriptEngine.DeserializationEnvironment = scriptEngine;
            var function = (FunctionInstance)Clone(scriptEngine.GetGlobalValue("outer"));

            // Verify it was deserialized correctly.
            Assert.AreEqual(11.0, function.Call(null, 5, 6));
        }

        [TestMethod]
        public void SerializeExternalClosure()
        {
            var scriptEngine = new ScriptEngine();
            scriptEngine.Execute(@"function makeClosure(a) { return function(b) { return a + b; } }");
            var makeClosureFunc = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure");
            var closureFunc = (FunctionInstance)makeClosureFunc.Call(null, 5);

            // Verify original closure is correct
            Assert.AreEqual(11.0, closureFunc.Call(null, 6));

            object[] serArray = new object[] { scriptEngine, closureFunc };
            object[] deserArray = (object[])SerializationTests.Clone(serArray);

            var scriptEngine2 = (ScriptEngine)deserArray[0];
            var closureFunc2 = (FunctionInstance)deserArray[1];

            // Verify deserialized closure is correct
            Assert.AreEqual(11.0, closureFunc2.Call(null, 6));
        }

        [TestMethod]
        public void SerializeInternalClosure()
        {
            var scriptEngine = new ScriptEngine();
            scriptEngine.Execute(@"
                            function makeClosure(a) { return function(b) { return a + b; } }

                            var closure = makeClosure(5);

                            function callClosure(c) { return closure(c); }");
            var callClosureFunc = (FunctionInstance)scriptEngine.GetGlobalValue("callClosure");

            // Verify original closure is correct
            Assert.AreEqual(11.0, callClosureFunc.Call(null, 6));

            var scriptEngine2 = (ScriptEngine)SerializationTests.Clone(scriptEngine);
            var callClosureFunc2 = (FunctionInstance)scriptEngine2.GetGlobalValue("callClosure");

            // Verify deserialized closure is correct
            Assert.AreEqual(11.0, callClosureFunc.Call(null, 6));
        }

        [TestMethod]
        public void SerializeExternalGenerator()
        {
            var scriptEngine = new ScriptEngine();
            scriptEngine.Execute(@"function makeGenerator(initialValue) { var curValue = initialValue; return function() { return curValue++; } }");
            var makeGeneratorFunc = (FunctionInstance)scriptEngine.GetGlobalValue("makeGenerator");
            var generatorFunc = (FunctionInstance)makeGeneratorFunc.Call(null, 5);

            // Verify original generator is correct
            Assert.AreEqual(5.0, generatorFunc.Call(null));
            Assert.AreEqual(6.0, generatorFunc.Call(null));
            Assert.AreEqual(7.0, generatorFunc.Call(null));

            object[] serArray = new object[] { scriptEngine, generatorFunc };
            object[] deserArray = (object[])SerializationTests.Clone(serArray);

            var scriptEngine2 = (ScriptEngine)deserArray[0];
            var generatorFunc2 = (FunctionInstance)deserArray[1];

            // Verify deserialized generator is correct
            Assert.AreEqual(8.0, generatorFunc2.Call(null));
            Assert.AreEqual(9.0, generatorFunc2.Call(null));
            Assert.AreEqual(10.0, generatorFunc2.Call(null));

            // Verify original generator didn't share state with deserialized generator
            Assert.AreEqual(8.0, generatorFunc.Call(null));
            Assert.AreEqual(9.0, generatorFunc.Call(null));
            Assert.AreEqual(10.0, generatorFunc.Call(null));

            // Verify deserialized generator didn't share state with original generator
            Assert.AreEqual(11.0, generatorFunc2.Call(null));
            Assert.AreEqual(12.0, generatorFunc2.Call(null));
            Assert.AreEqual(13.0, generatorFunc2.Call(null));
        }

        [TestMethod]
        public void SerializeInternalGenerator()
        {
            var scriptEngine = new ScriptEngine();
            scriptEngine.Execute(@"
                    function makeGenerator(initialValue) { var curValue = initialValue; return function() { return curValue++; } }

                    var generator = makeGenerator(5);

                    function callGenerator() { return generator(); }");
            var callGeneratorFunc = (FunctionInstance)scriptEngine.GetGlobalValue("callGenerator");

            // Verify original generator is correct
            Assert.AreEqual(5.0, callGeneratorFunc.Call(null));
            Assert.AreEqual(6.0, callGeneratorFunc.Call(null));
            Assert.AreEqual(7.0, callGeneratorFunc.Call(null));

            var scriptEngine2 = (ScriptEngine)SerializationTests.Clone(scriptEngine);
            var callGeneratorFunc2 = (FunctionInstance)scriptEngine2.GetGlobalValue("callGenerator");

            // Verify deserialized generator is correct
            Assert.AreEqual(8.0, callGeneratorFunc2.Call(null));
            Assert.AreEqual(9.0, callGeneratorFunc2.Call(null));
            Assert.AreEqual(10.0, callGeneratorFunc2.Call(null));

            // Verify original generator didn't share state with deserialized generator
            Assert.AreEqual(8.0, callGeneratorFunc.Call(null));
            Assert.AreEqual(9.0, callGeneratorFunc.Call(null));
            Assert.AreEqual(10.0, callGeneratorFunc.Call(null));

            // Verify deserialized generator didn't share state with original generator
            Assert.AreEqual(11.0, callGeneratorFunc2.Call(null));
            Assert.AreEqual(12.0, callGeneratorFunc2.Call(null));
            Assert.AreEqual(13.0, callGeneratorFunc2.Call(null));
        }

        [TestMethod]
        public void SerializedClosureScopes()
        {
            var scriptEngine = new ScriptEngine();
            scriptEngine.Execute(@"
                    var a = 'global';

                    function makeClosure1() { var a = 'ok1'; return function() { return a; } }

                    function makeClosure2(a) { return function() { return a; } }

                    function makeClosure3() { var a = 'not ok - func'; return (function() { var a = 'ok3'; return function() { return a; } })(); }

                    function makeClosure4(a) { return (function() { var a = 'ok4'; return function() { return a; } })(); }

                    function makeClosure5() { var a = 'not ok - func'; return function(b) { var a = b; return a; } }

                    function makeClosure6() { return function() { return a; } }");

            var makeClosure1Func = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure1");
            var makeClosure2Func = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure2");
            var makeClosure3Func = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure3");
            var makeClosure4Func = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure4");
            var makeClosure5Func = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure5");
            var makeClosure6Func = (FunctionInstance)scriptEngine.GetGlobalValue("makeClosure6");

            var closure1 = (FunctionInstance)makeClosure1Func.Call(null);
            var closure2 = (FunctionInstance)makeClosure2Func.Call(null, "ok2");
            var closure3 = (FunctionInstance)makeClosure3Func.Call(null);
            var closure4 = (FunctionInstance)makeClosure4Func.Call(null, "not ok - argument");
            var closure5 = (FunctionInstance)makeClosure5Func.Call(null);
            var closure6 = (FunctionInstance)makeClosure6Func.Call(null);

            // Verify original closures
            Assert.AreEqual("ok1", closure1.Call(null));
            Assert.AreEqual("ok2", closure2.Call(null, "ok2"));
            Assert.AreEqual("ok3", closure3.Call(null));
            Assert.AreEqual("ok4", closure4.Call(null, "not ok - argument"));
            Assert.AreEqual("ok5", closure5.Call(null, "ok5"));
            Assert.AreEqual("global", closure6.Call(null));

            object[] serArray = new object[] { scriptEngine, closure1, closure2, closure3, closure4, closure5, closure6 };
            object[] deserArray = (object[])SerializationTests.Clone(serArray);
            var scriptEngine2 = (ScriptEngine)deserArray[0];
            var deserClosure1 = (FunctionInstance)deserArray[1];
            var deserClosure2 = (FunctionInstance)deserArray[2];
            var deserClosure3 = (FunctionInstance)deserArray[3];
            var deserClosure4 = (FunctionInstance)deserArray[4];
            var deserClosure5 = (FunctionInstance)deserArray[5];
            var deserClosure6 = (FunctionInstance)deserArray[6];

            // Verify deserialized closures
            Assert.AreEqual("ok1", deserClosure1.Call(null));
            Assert.AreEqual("ok2", deserClosure2.Call(null, "ok2"));
            Assert.AreEqual("ok3", deserClosure3.Call(null));
            Assert.AreEqual("ok4", deserClosure4.Call(null, "not ok - argument"));
            Assert.AreEqual("ok5", deserClosure5.Call(null, "ok5"));
            Assert.AreEqual("global", deserClosure6.Call(null));
        }

        // Clone an object using serialization.
        private static object Clone(object objectToSerialize)
        {
            var serializer = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            var stream = new System.IO.MemoryStream();
            serializer.Serialize(stream, objectToSerialize);
            stream.Seek(0, System.IO.SeekOrigin.Begin);
            return serializer.Deserialize(stream);
        }
    }
}
