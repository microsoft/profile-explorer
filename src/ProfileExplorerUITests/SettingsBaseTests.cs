// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ProtoBuf;
using ProfileExplorer.UI; // For Utils
using ProfileExplorerCore2.Settings; // For SettingsBase, OptionValueAttribute
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProfileExplorerUITests.Settings;

[TestClass]
public class SettingsBaseTests {
  [ClassInitialize]
  public static void ClassInitialize(TestContext context) {
    // Register UI-specific type converters for the settings system
    SettingsTypeRegistry.RegisterConverter(new ProfileExplorerUI.Settings.ColorSettingsConverter());
  }

  [TestMethod]
  public void TestCollectOptions() {
    var data = new DerivedObject();
    var options = SettingsBase.CollectOptionMembers(data);
    Assert.AreEqual(options.Count, 8);

    int[]? expectedIds = new int[] {
      1, 2, 3, 4, 101, 102, 103, 104
    };

    foreach (int id in expectedIds) {
      var optionId = options.First(item => item.MemberId == id);
      Assert.IsNotNull(optionId);
      Assert.AreEqual(optionId.ClassName, "DerivedObject");
    }
  }

  [TestMethod]
  public void TestResetOptions() {
    var data = new TestObject();
    data.a = false;
    data.b = true;
    data.c = 456;
    data.s = "bar";

    data.Reset();
    Assert.AreEqual(data.a, true);
    Assert.AreEqual(data.b, false);
    Assert.AreEqual(data.c, 123);
    Assert.AreEqual(data.s, "foo");
  }

  [TestMethod]
  public void TestResetOptionsNested() {
    var data = new CombinedObject();
    data.nested = new NestedObject();
    data.nested.a = 789;
    data.nested.b = 101112;
    data.nested2 = new NestedObject();
    data.nested2.a = 131415;
    data.nested2.b = 161718;
    data.a = 192021;
    data.ns = new NonSettingsObject();
    data.ns.a = 222324;
    data.ns.b = 252627;

    data.Reset();
    Assert.AreEqual(data.nested.a, 123);
    Assert.AreEqual(data.nested.b, 456);
    Assert.AreEqual(data.nested2.a, 123);
    Assert.AreEqual(data.nested2.b, 456);
    Assert.AreEqual(data.a, 0);
    Assert.AreEqual(data.ns.a, 123);
    Assert.AreEqual(data.ns.b, 456);
  }

  [TestMethod]
  public void TestResetOptionsNestedDerived() {
    var data = new DerivedObject();
    data.a = false;
    data.b = true;
    data.c = 456;
    data.s = "bar";
    data.d = false;
    data.s2 = "baz";
    data.color = Colors.Black;
    data.colorArray = new Color[] {Colors.Black, Colors.White};

    data.Reset();
    Assert.AreEqual(data.a, true);
    Assert.AreEqual(data.b, false);
    Assert.AreEqual(data.c, 123);
    Assert.AreEqual(data.s, "foo");
    Assert.AreEqual(data.d, true);
    Assert.AreEqual(data.s2, "bar");
    Assert.AreEqual(data.color, Utils.ColorFromString("#F0F0F0"));
    Assert.AreEqual(data.colorArray[0], Utils.ColorFromString("#F0F0F0"));
    Assert.AreEqual(data.colorArray[1], Utils.ColorFromString("#1F2F3F"));
  }

  [TestMethod]
  public void TestResetOptionsCollection() {
    var data = new CollectionObject();
    data.list = new List<int> {1, 2, 3};
    data.dict = new Dictionary<string, int> {{"a", 1}, {"b", 2}};

    data.Reset();
    Assert.AreEqual(data.list.Count, 0);
    Assert.AreEqual(data.dict.Count, 0);
  }

  [TestMethod]
  public void TestConstructOptionsCollection() {
    var data = new CollectionObject();
    data.Reset();
    Assert.AreEqual(data.list.Count, 0);
    Assert.AreEqual(data.dict.Count, 0);
    Assert.IsTrue(data.flag);
  }

  [TestMethod]
  public void TestAreSettingsOptionsEqual() {
    var data1 = new TestObject();
    var data2 = new TestObject();
    data1.Reset();
    data2.Reset();
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.a = false;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.a = false;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.b = true;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.b = true;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.c = 456;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.c = 456;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.s = "bar";
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.s = "bar";
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));
  }

  [TestMethod]
  public void TestAreSettingsOptionsEqualNested() {
    var data1 = new CombinedObject();
    var data2 = new CombinedObject();
    data1.Reset();
    data2.Reset();
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));
    Assert.IsTrue(data1.nested.EqualsCalled);
    Assert.IsTrue(data1.nested2.EqualsCalled);

    data1.nested.a = 789;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.nested.a = 789;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.nested.b = 101112;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.nested.b = 101112;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.nested2.a = 131415;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.nested2.a = 131415;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.nested2.b = 161718;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.nested2.b = 161718;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.a = 192021;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.a = 192021;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.ns.a = 222324;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.ns.a = 222324;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));

    data1.ns.b = 252627;
    Assert.IsFalse(SettingsBase.AreOptionsEqual(data1, data2));

    data2.ns.b = 252627;
    Assert.IsTrue(SettingsBase.AreOptionsEqual(data1, data2));
  }

  [TestMethod]
  public void TestInitializeAllNewOptions() {
    var data = new DerivedObject();
    data.Reset();
    var options = SettingsBase.CollectOptionMembers(data);
    data.a = false;
    data.b = true;
    data.c = 456;
    data.s = "bar";

    options.RemoveWhere(item => item.MemberId >= 100);
    SettingsBase.InitializeAllNewOptions(data, options);
    Assert.AreEqual(data.a, true);
    Assert.AreEqual(data.b, false);
    Assert.AreEqual(data.c, 123);
    Assert.AreEqual(data.s, "foo");
  }

  [TestMethod]
  public void TestInitializeNoNewOptions() {
    var data = new DerivedObject();
    data.Reset();
    var options = SettingsBase.CollectOptionMembers(data);
    data.a = false;
    data.b = true;
    data.c = 456;
    data.s = "bar";

    SettingsBase.InitializeAllNewOptions(data, options);
    Assert.AreEqual(data.a, false);
    Assert.AreEqual(data.b, true);
    Assert.AreEqual(data.c, 456);
    Assert.AreEqual(data.s, "bar");
  }

  [TestMethod]
  public void TestInitializeAllNewOptionsNested() {
    var data = new CombinedObject();
    data.Reset();
    var options = SettingsBase.CollectOptionMembers(data);
    data.nested = new NestedObject();
    data.nested.a = 789;
    data.nested.b = 101112;
    data.nested2 = new NestedObject();
    data.nested2.a = 131415;
    data.nested2.b = 161718;

    options.RemoveWhere(item => item.ClassName == "NestedObject");
    SettingsBase.InitializeAllNewOptions(data, options);
    Assert.AreEqual(data.nested.a, 123);
    Assert.AreEqual(data.nested.b, 456);
    Assert.AreEqual(data.nested2.a, 123);
    Assert.AreEqual(data.nested2.b, 456);
  }

  [TestMethod]
  public void TestInitializeReferenceOptions() {
    var data = new CollectionObject();
    SettingsBase.InitializeReferenceOptions(data);
    Assert.IsNotNull(data.list);
    Assert.IsNotNull(data.dict);
    Assert.IsFalse(data.flag); // Should not be changed.
  }

  [ProtoContract()]
  [ProtoInclude(100, typeof(DerivedObject))]
  private class TestObject : SettingsBase {
    [ProtoMember(1)][OptionValue(true)]
    public bool a { get; set; }
    [ProtoMember(2)][OptionValue(false)]
    public bool b { get; set; }
    [ProtoMember(3)][OptionValue(123)]
    public int c { get; set; }
    [ProtoMember(4)][OptionValue("foo")]
    public string s { get; set; }

    public override void Reset() {
      ResetAllOptions(this, typeof(TestObject));
    }
  }

  [ProtoContract()]
  private class DerivedObject : TestObject {
    [ProtoMember(1)][OptionValue(true)]
    public bool d { get; set; }
    [ProtoMember(2)][OptionValue("bar")]
    public string s2 { get; set; }
    [ProtoMember(3)][OptionValue("#F0F0F0")]
    public Color color { get; set; }
    [ProtoMember(4)][OptionValue(new string[] {"#F0F0F0", "#1F2F3F"})]
    public Color[] colorArray { get; set; }

    public override void Reset() {
      base.Reset();
      ResetAllOptions(this);
    }
  }

  [ProtoContract()]
  private class NestedObject : SettingsBase {
    public bool EqualsCalled = false;
    [ProtoMember(1)][OptionValue(123)]
    public int a { get; set; }
    [ProtoMember(2)][OptionValue(456)]
    public int b { get; set; }

    public override void Reset() {
      ResetAllOptions(this);
    }

    public override bool Equals(object? obj) {
      EqualsCalled = true;
      return a == ((NestedObject)obj).a && b == ((NestedObject)obj).b;
    }
  }

  private class NonSettingsObject {
    public int a { get; set; } = 123;
    public int b { get; set; } = 456;

    public override bool Equals(object? obj) {
      return a == ((NonSettingsObject)obj).a && b == ((NonSettingsObject)obj).b;
    }
  }

  [ProtoContract()]
  private class CombinedObject : SettingsBase {
    [ProtoMember(1)]
    public NestedObject nested { get; set; }
    [ProtoMember(2)]
    public NestedObject nested2 { get; set; }
    [ProtoMember(3)]
    public int a { get; set; }
    [ProtoMember(4)]
    public NonSettingsObject ns { get; set; }
    [ProtoMember(5)]
    public TestObject test { get; set; }
    [ProtoMember(6)]
    public DerivedObject derived { get; set; }

    public override void Reset() {
      ResetAllOptions(this);
    }
  }

  [ProtoContract()]
  public class CollectionObject : SettingsBase {
    [ProtoMember(1)][OptionValue()]
    public List<int> list { get; set; }
    [ProtoMember(2)][OptionValue()]
    public Dictionary<string, int> dict { get; set; }
    [ProtoMember(3)][OptionValue(true)]
    public bool flag { get; set; }

    public override void Reset() {
      ResetAllOptions(this);
    }
  }
}