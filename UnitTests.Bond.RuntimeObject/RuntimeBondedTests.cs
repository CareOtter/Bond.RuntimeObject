namespace Bond.RuntimeObject.UnitTests
{
    using System.Collections.Generic;
    using Bond;
    using Bond.IO.Safe;
    using Bond.Protocols;
    using NUnit.Framework;

    [TestFixture]
    public class RuntimeBondedTests
    {
        [Test]
        public void Deserialize_ReturnsCorrectClonedObjectForStructWithBonded()
        {
            var schema = Schema<StructWithBonded>.RuntimeSchema;

            var expected = new RuntimeObject();

            var expectedField = new RuntimeObject
            {
                {
                    "basic",
                    new RuntimeObject
                    {
                        { "_bool", true },
                        { "_str", "First String" },
                        { "_wstr", "Second String" },
                        { "_int64", long.MaxValue },
                    }
                },
                {
                    "nested",
                    new RuntimeObject
                    {
                        {
                            "guid",
                            new RuntimeObject
                            {
                                { "Data1", (uint)1 },
                                { "Data2", (ushort)22 },
                                { "Data3", (ushort)333 },
                                { "Data4", (ulong)4444 },
                            }
                        }
                    }
                }
            };
            expected.Properties["field"] = new RuntimeBonded<RuntimeObject>(expectedField, schema,
                schema.SchemaDef.structs[schema.SchemaDef.root.struct_def].fields[0].type);

            var expectedPoly = new List<RuntimeBonded<RuntimeObject>>();

            var expectedPolyItem1 = new RuntimeObject
            {
                {
                    "basic",
                    new RuntimeObject
                    {
                        { "_bool", true },
                        { "_str", "Third String" },
                        { "_wstr", "Fourth String" },
                        { "_int64", long.MaxValue - 1 },
                    }
                },
                {
                    "nested",
                    new RuntimeObject
                    {
                        {
                            "guid",
                            new RuntimeObject
                            {
                                { "Data1", (uint)2 },
                                { "Data2", (ushort)33 },
                                { "Data3", (ushort)444 },
                                { "Data4", (ulong)5555 },
                            }
                        }
                    }
                },
            };
            expectedPoly.Add(new RuntimeBonded<RuntimeObject>(expectedPolyItem1, schema,
                schema.SchemaDef.structs[schema.SchemaDef.root.struct_def].fields[1].type.element));

            var expectedPolyItem2 = new RuntimeObject
            {
                {
                    "basic",
                    new RuntimeObject
                    {
                        { "_bool", true },
                        { "_str", "Fifth String" },
                        { "_wstr", "Sixth String" },
                        { "_int64", long.MaxValue - 2 },
                    }
                },
                {
                    "nested",
                    new RuntimeObject
                    {
                        {
                            "guid",
                            new RuntimeObject
                            {
                                { "Data1", (uint)3 },
                                { "Data2", (ushort)44 },
                                { "Data3", (ushort)555 },
                                { "Data4", (ulong)6666 },
                            }
                        }
                    }
                },
            };
            expectedPoly.Add(new RuntimeBonded<RuntimeObject>(expectedPolyItem2, schema,
                schema.SchemaDef.structs[schema.SchemaDef.root.struct_def].fields[1].type.element));

            expected.Properties["poly"] = expectedPoly;

            var target = new RuntimeBonded<RuntimeObject>(expected, schema, schema.SchemaDef.root);

            var actual = ((IRuntimeBonded<RuntimeObject>)target).Deserialize();

            Assert.AreNotSame(expected, actual);
            Assert.AreEqual(2, actual.Properties.Count);

            // field
            Assert.IsTrue(actual.Properties.ContainsKey("field"));
            Assert.IsTrue(actual.Properties["field"] is RuntimeBonded<RuntimeObject>);
            var actualField = ((IRuntimeBonded<RuntimeObject>)actual.Properties["field"]).Deserialize();

            // field.basic
            Assert.IsTrue(actualField.Properties.ContainsKey("basic"));
            Assert.IsTrue(actualField.Properties["basic"] is RuntimeObject);
            var actualFieldBasic = (RuntimeObject)actualField.Properties["basic"];

            Assert.IsTrue(actualFieldBasic.Properties.ContainsKey("_bool"));
            Assert.IsTrue(actualFieldBasic.Properties["_bool"] is bool);
            Assert.AreEqual(true, (bool)actualFieldBasic.Properties["_bool"]);

            Assert.IsTrue(actualFieldBasic.Properties.ContainsKey("_str"));
            Assert.IsTrue(actualFieldBasic.Properties["_str"] is string);
            Assert.AreEqual("First String", (string)actualFieldBasic.Properties["_str"]);

            Assert.IsTrue(actualFieldBasic.Properties.ContainsKey("_wstr"));
            Assert.IsTrue(actualFieldBasic.Properties["_wstr"] is string);
            Assert.AreEqual("Second String", (string)actualFieldBasic.Properties["_wstr"]);

            Assert.IsTrue(actualFieldBasic.Properties.ContainsKey("_int64"));
            Assert.IsTrue(actualFieldBasic.Properties["_int64"] is long);
            Assert.AreEqual(long.MaxValue, (long)actualFieldBasic.Properties["_int64"]);

            // field.nested
            Assert.IsTrue(actualField.Properties.ContainsKey("nested"));
            Assert.IsTrue(actualField.Properties["nested"] is RuntimeObject);
            var actualFieldNested = (RuntimeObject)actualField.Properties["nested"];

            // field.nested.guid
            Assert.IsTrue(actualFieldNested.Properties.ContainsKey("guid"));
            Assert.IsTrue(actualFieldNested.Properties["guid"] is RuntimeObject);
            var actualFieldNestedGuid = (RuntimeObject)actualFieldNested.Properties["guid"];

            Assert.IsTrue(actualFieldNestedGuid.Properties.ContainsKey("Data1"));
            Assert.IsTrue(actualFieldNestedGuid.Properties["Data1"] is uint);
            Assert.AreEqual(1U, (uint)actualFieldNestedGuid.Properties["Data1"]);

            Assert.IsTrue(actualFieldNestedGuid.Properties.ContainsKey("Data2"));
            Assert.IsTrue(actualFieldNestedGuid.Properties["Data2"] is ushort);
            Assert.AreEqual((ushort)22, (ushort)actualFieldNestedGuid.Properties["Data2"]);

            Assert.IsTrue(actualFieldNestedGuid.Properties.ContainsKey("Data3"));
            Assert.IsTrue(actualFieldNestedGuid.Properties["Data3"] is ushort);
            Assert.AreEqual((ushort)333, (ushort)actualFieldNestedGuid.Properties["Data3"]);

            Assert.IsTrue(actualFieldNestedGuid.Properties.ContainsKey("Data4"));
            Assert.IsTrue(actualFieldNestedGuid.Properties["Data4"] is ulong);
            Assert.AreEqual(4444UL, (ulong)actualFieldNestedGuid.Properties["Data4"]);

            // poly
            Assert.IsTrue(actual.Properties.ContainsKey("poly"));
            Assert.IsTrue(actual.Properties["poly"] is List<RuntimeBonded<RuntimeObject>>);
            var actualPoly = (List<RuntimeBonded<RuntimeObject>>)actual.Properties["poly"];

            Assert.AreEqual(2, actualPoly.Count);

            // poly[0]
            var actualPolyItem0 = ((IRuntimeBonded<RuntimeObject>)actualPoly[0]).Deserialize();

            // poly[0].basic
            Assert.IsTrue(actualField.Properties.ContainsKey("basic"));
            Assert.IsTrue(actualField.Properties["basic"] is RuntimeObject);
            var actualPolyItem0Basic = (RuntimeObject)actualPolyItem0.Properties["basic"];

            Assert.IsTrue(actualPolyItem0Basic.Properties.ContainsKey("_bool"));
            Assert.IsTrue(actualPolyItem0Basic.Properties["_bool"] is bool);
            Assert.AreEqual(true, (bool)actualPolyItem0Basic.Properties["_bool"]);

            Assert.IsTrue(actualPolyItem0Basic.Properties.ContainsKey("_str"));
            Assert.IsTrue(actualPolyItem0Basic.Properties["_str"] is string);
            Assert.AreEqual("Third String", (string)actualPolyItem0Basic.Properties["_str"]);

            Assert.IsTrue(actualPolyItem0Basic.Properties.ContainsKey("_wstr"));
            Assert.IsTrue(actualPolyItem0Basic.Properties["_wstr"] is string);
            Assert.AreEqual("Fourth String", (string)actualPolyItem0Basic.Properties["_wstr"]);

            Assert.IsTrue(actualPolyItem0Basic.Properties.ContainsKey("_int64"));
            Assert.IsTrue(actualPolyItem0Basic.Properties["_int64"] is long);
            Assert.AreEqual(long.MaxValue - 1, (long)actualPolyItem0Basic.Properties["_int64"]);

            // poly[0].nested
            Assert.IsTrue(actualPolyItem0.Properties.ContainsKey("nested"));
            Assert.IsTrue(actualPolyItem0.Properties["nested"] is RuntimeObject);
            var actualPolyItem0Nested = (RuntimeObject)actualPolyItem0.Properties["nested"];

            // poly[0].nested.guid
            Assert.IsTrue(actualPolyItem0Nested.Properties.ContainsKey("guid"));
            Assert.IsTrue(actualPolyItem0Nested.Properties["guid"] is RuntimeObject);
            var actualPolyItem0NestedGuid = (RuntimeObject)actualPolyItem0Nested.Properties["guid"];

            Assert.IsTrue(actualPolyItem0NestedGuid.Properties.ContainsKey("Data1"));
            Assert.IsTrue(actualPolyItem0NestedGuid.Properties["Data1"] is uint);
            Assert.AreEqual(2U, (uint)actualPolyItem0NestedGuid.Properties["Data1"]);

            Assert.IsTrue(actualPolyItem0NestedGuid.Properties.ContainsKey("Data2"));
            Assert.IsTrue(actualPolyItem0NestedGuid.Properties["Data2"] is ushort);
            Assert.AreEqual((ushort)33, (ushort)actualPolyItem0NestedGuid.Properties["Data2"]);

            Assert.IsTrue(actualPolyItem0NestedGuid.Properties.ContainsKey("Data3"));
            Assert.IsTrue(actualPolyItem0NestedGuid.Properties["Data3"] is ushort);
            Assert.AreEqual((ushort)444, (ushort)actualPolyItem0NestedGuid.Properties["Data3"]);

            Assert.IsTrue(actualPolyItem0NestedGuid.Properties.ContainsKey("Data4"));
            Assert.IsTrue(actualPolyItem0NestedGuid.Properties["Data4"] is ulong);
            Assert.AreEqual(5555UL, (ulong)actualPolyItem0NestedGuid.Properties["Data4"]);

            // poly[1]
            var actualPolyItem1 = ((IRuntimeBonded<RuntimeObject>)actualPoly[1]).Deserialize();

            // poly[1].basic
            Assert.IsTrue(actualField.Properties.ContainsKey("basic"));
            Assert.IsTrue(actualField.Properties["basic"] is RuntimeObject);
            var actualPolyItem1Basic = (RuntimeObject)actualPolyItem1.Properties["basic"];

            Assert.IsTrue(actualPolyItem1Basic.Properties.ContainsKey("_bool"));
            Assert.IsTrue(actualPolyItem1Basic.Properties["_bool"] is bool);
            Assert.AreEqual(true, (bool)actualPolyItem1Basic.Properties["_bool"]);

            Assert.IsTrue(actualPolyItem1Basic.Properties.ContainsKey("_str"));
            Assert.IsTrue(actualPolyItem1Basic.Properties["_str"] is string);
            Assert.AreEqual("Fifth String", (string)actualPolyItem1Basic.Properties["_str"]);

            Assert.IsTrue(actualPolyItem1Basic.Properties.ContainsKey("_wstr"));
            Assert.IsTrue(actualPolyItem1Basic.Properties["_wstr"] is string);
            Assert.AreEqual("Sixth String", (string)actualPolyItem1Basic.Properties["_wstr"]);

            Assert.IsTrue(actualPolyItem1Basic.Properties.ContainsKey("_int64"));
            Assert.IsTrue(actualPolyItem1Basic.Properties["_int64"] is long);
            Assert.AreEqual(long.MaxValue - 2, (long)actualPolyItem1Basic.Properties["_int64"]);

            // poly[1].nested
            Assert.IsTrue(actualPolyItem1.Properties.ContainsKey("nested"));
            Assert.IsTrue(actualPolyItem1.Properties["nested"] is RuntimeObject);
            var actualPolyItem1Nested = (RuntimeObject)actualPolyItem1.Properties["nested"];

            // poly[1].nested.guid
            Assert.IsTrue(actualPolyItem1Nested.Properties.ContainsKey("guid"));
            Assert.IsTrue(actualPolyItem1Nested.Properties["guid"] is RuntimeObject);
            var actualPolyItem1NestedGuid = (RuntimeObject)actualPolyItem1Nested.Properties["guid"];

            Assert.IsTrue(actualPolyItem1NestedGuid.Properties.ContainsKey("Data1"));
            Assert.IsTrue(actualPolyItem1NestedGuid.Properties["Data1"] is uint);
            Assert.AreEqual(3U, (uint)actualPolyItem1NestedGuid.Properties["Data1"]);

            Assert.IsTrue(actualPolyItem1NestedGuid.Properties.ContainsKey("Data2"));
            Assert.IsTrue(actualPolyItem1NestedGuid.Properties["Data2"] is ushort);
            Assert.AreEqual((ushort)44, (ushort)actualPolyItem1NestedGuid.Properties["Data2"]);

            Assert.IsTrue(actualPolyItem1NestedGuid.Properties.ContainsKey("Data3"));
            Assert.IsTrue(actualPolyItem1NestedGuid.Properties["Data3"] is ushort);
            Assert.AreEqual((ushort)555, (ushort)actualPolyItem1NestedGuid.Properties["Data3"]);

            Assert.IsTrue(actualPolyItem1NestedGuid.Properties.ContainsKey("Data4"));
            Assert.IsTrue(actualPolyItem1NestedGuid.Properties["Data4"] is ulong);
            Assert.AreEqual(6666UL, (ulong)actualPolyItem1NestedGuid.Properties["Data4"]);
        }

        [Test]
        public void Deserialize_ReturnsCorrectObjectForSerializedBasicTypes()
        {
            var schema = Schema<BasicTypes>.RuntimeSchema;

            var original = new BasicTypes
            {
                _bool = true,
                _str = "First String",
                _wstr = "Second String",
                _int64 = long.MaxValue,
            };

            var reader = SerializeAndCreateReader(original);
            var target = new RuntimeBonded<RuntimeObject, CompactBinaryReader<InputBuffer>>(reader, schema,
                schema.SchemaDef.root);

            object actual = ((IRuntimeBonded)target).Deserialize();

            Assert.IsInstanceOf<RuntimeObject>(actual);
            var actualRuntimeObject = (RuntimeObject)actual;

            Assert.AreEqual(4, actualRuntimeObject.Properties.Count);

            Assert.IsTrue(actualRuntimeObject.Properties.ContainsKey("_bool"));
            Assert.IsTrue(actualRuntimeObject.Properties["_bool"] is bool);
            Assert.AreEqual(true, (bool)actualRuntimeObject.Properties["_bool"]);

            Assert.IsTrue(actualRuntimeObject.Properties.ContainsKey("_str"));
            Assert.IsTrue(actualRuntimeObject.Properties["_str"] is string);
            Assert.AreEqual("First String", (string)actualRuntimeObject.Properties["_str"]);

            Assert.IsTrue(actualRuntimeObject.Properties.ContainsKey("_wstr"));
            Assert.IsTrue(actualRuntimeObject.Properties["_wstr"] is string);
            Assert.AreEqual("Second String", (string)actualRuntimeObject.Properties["_wstr"]);

            Assert.IsTrue(actualRuntimeObject.Properties.ContainsKey("_int64"));
            Assert.IsTrue(actualRuntimeObject.Properties["_int64"] is long);
            Assert.AreEqual(long.MaxValue, (long)actualRuntimeObject.Properties["_int64"]);
        }

        private CompactBinaryReader<InputBuffer> SerializeAndCreateReader<T>(T original)
        {
            var outputBuffer = new OutputBuffer();
            var writer = new CompactBinaryWriter<OutputBuffer>(outputBuffer);
            Serialize.To(writer, original);

            return new CompactBinaryReader<InputBuffer>(new InputBuffer(outputBuffer.Data));
        }
    }
}
