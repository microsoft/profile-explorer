// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.UI.Document;
using ProfileExplorer.UI.Profile.Document;

namespace ProfileExplorer.UI.Profile;

public record FunctionMarkingCategory(
  FunctionMarkingStyle Marking,
  TimeSpan Weight,
  double Percentage,
  ProfileCallTreeNode HottestFunction,
  List<ProfileCallTreeNode> SortedFunctions) {
  public virtual bool Equals(FunctionMarkingCategory other) {
    if (ReferenceEquals(null, other)) return false;
    if (ReferenceEquals(this, other)) return true;
    return Equals(Marking, other.Marking) &&
           Weight.Equals(other.Weight) && Equals(HottestFunction, other.HottestFunction) &&
           SortedFunctions.AreEqual(other.SortedFunctions);
  }
}

public static class ProfilingUtils {
  public static void CreateInstancesMenu(MenuItem menu, IRTextSection section,
                                         FunctionProfileData funcProfile,
                                         RoutedEventHandler menuClickHandler,
                                         MouseButtonEventHandler menuRightClickHandler,
                                         TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    int order = 0;
    double maxWidth = 0;

    var nodes = session.ProfileData.CallTree.GetSortedCallTreeNodes(section.ParentFunction);
    int maxCallers = nodes.Count >= 2 ? CommonParentCallerIndex(nodes[0], nodes[1]) : int.MaxValue;

    foreach (var node in nodes) {
      double weightPercentage = funcProfile.ScaleWeight(node.Weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage) ||
          !node.HasCallers) {
        break;
      }

      (string title, string tooltip) = GenerateInstancePreviewText(node, session, maxCallers);
      string text = $"({markerSettings.FormatWeightValue(node.Weight)})";

      var value = new ProfileMenuItem(text, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
      };

      var item = new MenuItem {
        Header = value,
        IsCheckable = true,
        StaysOpenOnClick = true,
        Tag = node,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;

      if (menuRightClickHandler != null) {
        item.PreviewMouseRightButtonUp += menuRightClickHandler;
      }

      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static void CreateThreadsMenu(MenuItem menu, IRTextSection section,
                                       FunctionProfileData funcProfile,
                                       RoutedEventHandler menuClickHandler,
                                       TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();

    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    var timelineSettings = App.Settings.TimelineSettings;
    int order = 0;
    double maxWidth = 0;

    var node = session.ProfileData.CallTree.GetCombinedCallTreeNode(section.ParentFunction);

    foreach (var thread in node.SortedByWeightPerThreadWeights) {
      double weightPercentage = funcProfile.ScaleWeight(thread.Values.Weight);

      var threadInfo = session.ProfileData.FindThread(thread.ThreadId);
      var backColor = timelineSettings.GetThreadBackgroundColors(threadInfo, thread.ThreadId).Margin;

      string text = $"({markerSettings.FormatWeightValue(thread.Values.Weight)})";
      string tooltip = threadInfo is {HasName: true} ? threadInfo.Name : null;
      string title = !string.IsNullOrEmpty(tooltip) ? $"{thread.ThreadId} ({tooltip})" : $"{thread.ThreadId}";

      var value = new ProfileMenuItem(text, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
      };

      var item = new MenuItem {
        Header = value,
        IsCheckable = true,
        StaysOpenOnClick = true,
        Tag = thread.ThreadId,
        Background = backColor,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static void CreateInlineesMenu(MenuItem menu, IRTextSection section,
                                        List<InlineeListItem> inlineeList,
                                        FunctionProfileData funcProfile,
                                        RoutedEventHandler menuClickHandler,
                                        TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    int order = 0;
    double maxWidth = 0;

    // Compute time spent in non-inlinee parts.
    var inlineeWeightSum = TimeSpan.Zero;

    foreach (var node in inlineeList) {
      inlineeWeightSum += node.ExclusiveWeight;
    }

    var nonInlineeWeight = funcProfile.Weight - inlineeWeightSum;
    double nonInlineeWeightPercentage = funcProfile.ScaleWeight(nonInlineeWeight);
    string nonInlineeText = $"({markerSettings.FormatWeightValue(nonInlineeWeight)})";

    var nonInlineeValue = new ProfileMenuItem(nonInlineeText, nonInlineeWeight.Ticks, nonInlineeWeightPercentage) {
      PrefixText = "Non-Inlinee Code",
      ToolTip = "Time for code not originating from an inlined function",
      ShowPercentageBar = markerSettings.ShowPercentageBar(nonInlineeWeightPercentage),
      TextWeight = markerSettings.PickTextWeight(nonInlineeWeightPercentage),
      PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
    };
    profileItems.Add(nonInlineeValue);

    if (defaultItems.Count > 0 &&
        defaultItems[0] is MenuItem nonInlineeItem) {
      nonInlineeItem.Header = nonInlineeValue;
      nonInlineeItem.HeaderTemplate = valueTemplate;
    }

    foreach (var node in inlineeList) {
      double weightPercentage = funcProfile.ScaleWeight(node.ExclusiveWeight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      string title = node.InlineeFrame.Function.FormatFunctionName(session, 80);
      string text = $"({markerSettings.FormatWeightValue(node.ExclusiveWeight)})";
      string tooltip = $"File {Utils.TryGetFileName(node.InlineeFrame.FilePath)}:{node.InlineeFrame.Line}\n";
      tooltip += CreateInlineeFunctionDescription(node, funcProfile, settings.ProfileMarkerSettings, session);

      tooltip += $"\n\nFile path: {node.InlineeFrame.FilePath}";
      tooltip += $"\nFunction: {node.InlineeFrame.Function.FormatFunctionName(session)}";

      var value = new ProfileMenuItem(text, node.ExclusiveWeight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
      };

      var item = new MenuItem {
        Header = value,
        Tag = node,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    // If no items were added (besides "Non-Inlinee Code"),
    // add an entry about there being no significant inlinees.
    if (profileItems.Count == 1) {
      defaultItems.Add(new MenuItem() {
        Header = "No significant inlined functions",
        IsHitTestVisible = false,
        Tag = true, // Give it a tag so it can be removed later.
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      });
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static async Task PopulateMarkedModulesMenu(MenuItem menu, FunctionMarkingSettings settings,
                                                     ISession session, object triggerObject,
                                                     Func<Task> changedHandler) {
    if (IsTopLevelSubmenu(triggerObject)) return;

    await CreateMarkedModulesMenu(menu,
                            async (o, args) => {
                              if (args.Source is MenuItem menuItem) {
                                if (menuItem.Tag is FunctionMarkingStyle style) {
                                  style.IsEnabled = menuItem.IsChecked;

                                  if (style.IsEnabled) {
                                    settings.UseModuleColors = true;
                                  }

                                  await changedHandler();
                                }
                                else if (menuItem.Tag is IRTextFunction func) {
                                  // Click on submenu with individual functions.
                                  await session.SwitchActiveFunction(func);
                                }
                              }
                            },
                            (o, args) => {
                              if (args.Source is MenuItem menuItem) {
                                var style = menuItem.Tag as FunctionMarkingStyle;
                                settings.ModuleColors.Remove(style);
                                menu.IsSubmenuOpen = false;
                                changedHandler();
                              }
                            },
                            settings, session);
  }

  public static async Task PopulateMarkedFunctionsMenu(MenuItem menu, FunctionMarkingSettings settings,
                                                       ISession session, object triggerObject,
                                                       Func<Task> changedHandler) {
    if (IsTopLevelSubmenu(triggerObject)) return;

    await CreateMarkedFunctionsMenu(menu,
                                    async (o, args) => {
                                      if (args.Source is MenuItem menuItem) {
                                        if (menuItem.Tag is FunctionMarkingStyle style) {
                                          style.IsEnabled = menuItem.IsChecked;

                                          if (style.IsEnabled) {
                                            settings.UseFunctionColors = true;
                                          }

                                          await changedHandler();
                                        }
                                        else if (menuItem.Tag is IRTextFunction func) {
                                          // Click on submenu with individual functions.
                                          await session.SwitchActiveFunction(func);
                                        }
                                      }
                                    },
                                    (o, args) => {
                                      if (args.Source is MenuItem menuItem) {
                                        var style = menuItem.Tag as FunctionMarkingStyle;
                                        settings.FunctionColors.Remove(style);
                                        menu.IsSubmenuOpen = false;
                                        changedHandler();
                                      }
                                    },
                                    settings, session);
  }

  private static bool IsTopLevelSubmenu(object triggerObject) {
    // The OnSubmenuOpened event is triggered not only for the top-level menu,
    // but also for any submenus added from code-behind. Those need to be ignored
    // because otherwise the entire menu gets created below again, and it also gets hidden.
    if (triggerObject is not MenuItem triggerMenu ||
        triggerMenu.Tag != null) {
      return true;
    }

    return false;
  }

  public static async Task
    CreateMarkedModulesMenu(MenuItem menu,
                            MouseButtonEventHandler menuClickHandler,
                            MouseButtonEventHandler menuRightClickHandler,
                            FunctionMarkingSettings settings, ISession session) {
    var categories = await CreateModuleMarkingCategories(settings, session);
    CreateCategoriesMenu(categories, menu, false, menuClickHandler, menuRightClickHandler, session);
  }

  public static async Task<List<FunctionMarkingCategory>> CreateModuleMarkingCategories(
    FunctionMarkingSettings settings, ISession session) {
    var sortedModules = new List<(FunctionMarkingStyle Module, TimeSpan Weight)>();

    foreach (var moduleStyle in settings.ModuleColors) {
      var moduleWeight = session.ProfileData.FindModulesWeight(name =>
                                                                 moduleStyle.NameMatches(name));
      sortedModules.Add((moduleStyle, moduleWeight));
    }

    sortedModules.Sort((a, b) => a.Weight.CompareTo(b.Weight));
    var moduleFuncs = await Task.Run(() => CollectModuleSortedFunctions(session, 10));

    var categories = new List<FunctionMarkingCategory>();

    foreach (var pair in sortedModules) {
      double weightPercentage = session.ProfileData.ScaleModuleWeight(pair.Weight);
      var functs = moduleFuncs.GetValueOrNull(pair.Module.Name);
      var hottestFunc = functs?.Count > 0 ? functs[0] : null;
      var category = new FunctionMarkingCategory(pair.Module, pair.Weight, weightPercentage,
                                                 hottestFunc, functs);
      categories.Add(category);
    }

    categories.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    return categories;
  }

  public static async Task CreateMarkedFunctionsMenu(MenuItem menu,
                                                     MouseButtonEventHandler menuClickHandler,
                                                     MouseButtonEventHandler menuRightClickHandler,
                                                     FunctionMarkingSettings settings, ISession session) {
    await CreateMarkedFunctionsMenu(menu, false, menuClickHandler, menuRightClickHandler,
                                    settings.FunctionColors, session);
  }

  public static async Task
    CreateFunctionsCategoriesMenu(MenuItem menu,
                                  MouseButtonEventHandler menuClickHandler,
                                  MouseButtonEventHandler menuRightClickHandler,
                                  FunctionMarkingSettings settings, ISession session) {
    await CreateMarkedFunctionsMenu(menu, true, menuClickHandler, menuRightClickHandler,
                                    settings.BuiltinMarkingCategories.FunctionColors, session);
  }

  public static List<FunctionMarkingCategory> CollectMarkedFunctions(List<FunctionMarkingStyle> markings,
                                                                     bool isCategoriesMenu, ISession session,
                                                                     ProfileCallTreeNode startNode = null) {
    // Collect functions across all markings to compute the "Unmarked" weight.
    var markingCategoryList = new List<FunctionMarkingCategory>();
    object lockObject = new();
    var tasks = new List<Task>();

    foreach (var marking in markings) {
      tasks.Add(Task.Run(() => {
        // Find all functions matching the marked name. There can be multiple
        // since the same func. name may be used in multiple modules,
        // and also because the name matching may use Regex.
        var funcNodeList = new List<ProfileCallTreeNode>();
        var funcMap = new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();

        if (startNode == null) {
          CollectGlobalMarkedFunctions(marking, funcNodeList, funcMap, session);
        }
        else {
          CollectCallTreeMarkedFunctions(marking, startNode, funcNodeList, funcMap, session);
        }

        // Combine all marked functions to obtain the proper total weight.
        var weight = ProfileCallTree.CombinedCallTreeNodesWeight(funcNodeList);
        double weightPercentage = session.ProfileData.ScaleFunctionWeight(weight);
        var funcList = new List<ProfileCallTreeNode>();

        foreach (var pair in funcMap) {
          funcList.Add(ProfileCallTree.CombinedCallTreeNodes(pair.Value, false));
        }

        funcList.Sort((a, b) =>
                        b.Weight.CompareTo(a.Weight));
        var hottestFunc = funcList.Count > 0 ? funcList[0] : null;

        lock (lockObject) {
          markingCategoryList.Add(new FunctionMarkingCategory(marking, weight, weightPercentage,
                                                              hottestFunc, funcList));
        }
      }));
    }

    // Sort markings by weight in decreasing order.
    Task.WaitAll(tasks.ToArray());
    markingCategoryList.Sort((a, b) => b.Weight.CompareTo(a.Weight));

    if (isCategoriesMenu) {
      // Compute the "Unmarked" weight and add it as the last entry.
      var allFuncNodeList = new List<ProfileCallTreeNode>();

      foreach (var category in markingCategoryList) {
        allFuncNodeList.AddRange(category.SortedFunctions);
      }

      var categoriesWeight = ProfileCallTree.CombinedCallTreeNodesWeight(allFuncNodeList);
      var otherWeight = session.ProfileData.TotalWeight - categoriesWeight;
      double otherWeightPercentage = session.ProfileData.ScaleFunctionWeight(otherWeight);
      var uncategorizedMarking = new FunctionMarkingCategory(
        new FunctionMarkingStyle("Other functions not covered by categories",
                                 Colors.Transparent, "Uncategorized"),
        otherWeight, otherWeightPercentage, null, null);
      markingCategoryList.Add(uncategorizedMarking);
    }

    return markingCategoryList;
  }

  private static void CollectCallTreeMarkedFunctions(FunctionMarkingStyle marking, ProfileCallTreeNode startNode,
                                                     List<ProfileCallTreeNode> funcNodeList,
                                                     Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcNodeMap,
                                                     ISession session) {
    var nameProvider = session.CompilerInfo.NameProvider;
    var visited = new HashSet<ProfileCallTreeNode>();
    var queue = new Queue<ProfileCallTreeNode>();
    queue.Enqueue(startNode);

    while (queue.Count > 0) {
      var node = queue.Dequeue();

#if DEBUG
      Debug.Assert(!visited.Contains(node), "Cycle detected in call tree");
      visited.Add(node);
#endif

      if (marking.NameMatches(nameProvider.FormatFunctionName(node.Function.Name))) {
        funcNodeList.Add(node); // Per-category list.
        var instanceNodeList = funcNodeMap.GetOrAddValue(node.Function);
        instanceNodeList.Add(node);
      }

      if (node.HasChildren) {
        foreach (var child in node.Children) {
          queue.Enqueue(child);
        }
      }
    }
  }

  private static void CollectGlobalMarkedFunctions(FunctionMarkingStyle marking,
                                                   List<ProfileCallTreeNode> funcNodeList,
                                                   Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcNodeMap,
                                                   ISession session) {
    var nameProvider = session.CompilerInfo.NameProvider;

    foreach (var loadedDoc in session.SessionState.Documents) {
      if (loadedDoc.Summary == null) {
        continue;
      }

      var matchingFuncList = loadedDoc.Summary.FindFunctions(name =>
                                                               marking.NameMatches(
                                                                 nameProvider.FormatFunctionName(name)));

      foreach (var func in matchingFuncList) {
        var nodeList = session.ProfileData.CallTree.GetCallTreeNodes(func);

        if (nodeList != null) {
          funcNodeList.AddRange(nodeList); // Per-category list.
        }

        funcNodeMap[func] = nodeList;
      }
    }
  }

  private static Dictionary<string, List<ProfileCallTreeNode>>
    CollectModuleSortedFunctions(ISession session, int maxFunctsPerModule) {
    var functs = session.ProfileData.GetSortedFunctions();
    var moduleFuncs = new Dictionary<string, List<ProfileCallTreeNode>>();

    // Pick top N function per module, in exclusive weight order.
    foreach (var pair in functs) {
      var func = pair.Item1;
      var list = moduleFuncs.GetOrAddValue(func.ModuleName);

      if (list.Count < maxFunctsPerModule) {
        list.Add(session.ProfileData.CallTree.GetCombinedCallTreeNode(func));
      }
    }

    // Sort functions by weight in decreasing order.
    foreach (var pair in moduleFuncs) {
      pair.Value.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
    }

    return moduleFuncs;
  }

  private static int CommonParentCallerIndex(ProfileCallTreeNode a, ProfileCallTreeNode b) {
    int index = 0;

    do {
      index++;
      a = a.Caller;
      b = b.Caller;
    } while (a != b && a != null && b != null);

    return index;
  }

  public static (string Short, string Long)
    GenerateInstancePreviewText(ProfileCallTreeNode node, ISession session, int maxCallers = int.MaxValue) {
    return GenerateInstancePreviewText(node, maxCallers, 80, 25, 1000, 50, session);
  }

  private static (string Short, string Long)
    GenerateInstancePreviewText(ProfileCallTreeNode node, int maxCallers,
                                int maxLength, int maxSingleLength,
                                int maxCompleteLength, int maxCompleteLineLength, ISession session) {
    const string Separator = " \ud83e\udc70 "; // Arrow character.
    var sb = new StringBuilder();
    var completeSb = new StringBuilder();
    var nameProvider = session.CompilerInfo.NameProvider;
    int remaining = maxLength;
    int completeRemaining = maxCompleteLength;
    int completeLineRemaining = maxCompleteLineLength;
    int index = 0;
    node = node.Caller;

    completeSb.AppendLine("Right-click to select instance in Flame Graph and Call Tree\n");

    while (node != null) {
      // Build the shorter title stack trace.
      if (index < maxCallers && remaining > 0) {
        int maxNameLength = Math.Min(remaining, maxSingleLength);
        string name = node.FormatFunctionName(nameProvider.FormatFunctionName, maxNameLength);
        remaining -= name.Length;

        if (index == 0) {
          sb.Append(name);
        }
        else {
          sb.Append($"{Separator}{name}");
        }
      }

      // Build the longer tooltip stack trace.
      if (completeRemaining > 0) {
        int maxNameLength = Math.Min(completeRemaining, maxSingleLength);
        string name = node.FormatFunctionName(nameProvider.FormatFunctionName, maxNameLength);

        if (index == 0) {
          completeSb.Append(name);
        }
        else {
          completeSb.Append($"{Separator}{name}");
        }

        completeRemaining -= name.Length;
        completeLineRemaining -= name.Length;

        if (completeLineRemaining < 0) {
          completeSb.Append("\n");
          completeLineRemaining = maxCompleteLineLength;
        }
      }

      node = node.Caller;
      index++;
    }

    return (sb.ToString().Trim(), completeSb.ToString().Trim());
  }

  public static void HandleInstanceMenuItemChanged(MenuItem menuItem, MenuItem menu,
                                                   ProfileSampleFilter instanceFilter) {
    if (menuItem.Tag is ProfileCallTreeNode node) {
      instanceFilter ??= new ProfileSampleFilter();

      if (menuItem.IsChecked) {
        instanceFilter.AddInstance(node);
      }
      else {
        instanceFilter.RemoveInstance(node);
      }
    }
    else {
      instanceFilter.ClearInstances();
      UncheckMenuItems(menu, menuItem);
    }
  }

  public static void HandleThreadMenuItemChanged(MenuItem menuItem, MenuItem menu,
                                                 ProfileSampleFilter instanceFilter) {
    if (menuItem.Tag is int threadId) {
      instanceFilter ??= new ProfileSampleFilter();

      if (menuItem.IsChecked) {
        instanceFilter.AddThread(threadId);
      }
      else {
        instanceFilter.RemoveThread(threadId);
      }
    }
    else {
      instanceFilter.ClearThreads();
      UncheckMenuItems(menu, menuItem);
    }
  }

  public static void SyncThreadsMenuWithFilter(MenuItem menu, ProfileSampleFilter instanceFilter) {
    foreach (object item in menu.Items) {
      if (item is MenuItem menuItem && menuItem.Tag is int threadId) {
        menuItem.IsChecked = instanceFilter != null && instanceFilter.IncludesThread(threadId);
      }
    }
  }

  public static string CreateProfileFilterTitle(ProfileSampleFilter instanceFilter, ISession session) {
    if (instanceFilter == null) {
      return "";
    }

    return !instanceFilter.IncludesAll ? "Instance: " : "";
  }

  public static string CreateProfileFilterDescription(ProfileSampleFilter instanceFilter, ISession session) {
    if (instanceFilter == null) {
      return "";
    }

    var sb = new StringBuilder("\n");

    if (instanceFilter.HasInstanceFilter) {
      sb.AppendLine("\nInstances included:");

      foreach (var node in instanceFilter.FunctionInstances) {
        sb.AppendLine($" - {GenerateInstancePreviewText(node, session).Short}");
      }
    }

    if (instanceFilter.HasThreadFilter) {
      sb.AppendLine("\nThreads included:");

      foreach (int threadId in instanceFilter.ThreadIds) {
        var threadInfo = session.ProfileData.FindThread(threadId);
        string threadName = threadInfo is {HasName: true} ? threadInfo.Name : null;

        if (!string.IsNullOrEmpty(threadName)) {
          sb.AppendLine($" - {threadId} ({threadName})");
        }
        else {
          sb.AppendLine($" - {threadId}");
        }
      }
    }

    return sb.ToString().Trim();
  }

  public static string CreateProfileFunctionDescription(FunctionProfileData funcProfile,
                                                        ProfileDocumentMarkerSettings settings, ISession session) {
    return CreateProfileDescription(funcProfile.Weight, funcProfile.ExclusiveWeight,
                                    settings, session.ProfileData.ScaleFunctionWeight);
  }

  public static string CreateInlineeFunctionDescription(InlineeListItem inlinee,
                                                        FunctionProfileData funcProfile,
                                                        ProfileDocumentMarkerSettings settings, ISession session) {
    return CreateProfileDescription(inlinee.Weight, inlinee.ExclusiveWeight,
                                    settings, funcProfile.ScaleWeight);
  }

  public static string CreateProfileDescription(TimeSpan weight, TimeSpan exclusiveWeight,
                                                ProfileDocumentMarkerSettings settings,
                                                Func<TimeSpan, double> weightFunc) {
    double weightPerc = weightFunc(weight);
    double exclusiveWeightPerc = weightFunc(exclusiveWeight);
    string weightText = $"{weightPerc.AsPercentageString()} ({settings.FormatWeightValue(weight)})";
    string exclusiveWeightText =
      $"{exclusiveWeightPerc.AsPercentageString()} ({settings.FormatWeightValue(exclusiveWeight)})";
    return $"Total time: {weightText}\nSelf time: {exclusiveWeightText}";
  }

  public static void SyncInstancesMenuWithFilter(MenuItem menu, ProfileSampleFilter instanceFilter) {
    foreach (object item in menu.Items) {
      if (item is MenuItem menuItem && menuItem.Tag is ProfileCallTreeNode node) {
        menuItem.IsChecked = instanceFilter != null && instanceFilter.IncludesInstance(node);
      }
    }
  }

  public static async Task
    CreateMarkedFunctionsMenu(MenuItem menu, bool isCategoriesMenu,
                              MouseButtonEventHandler menuClickHandler,
                              MouseButtonEventHandler menuRightClickHandler,
                              List<FunctionMarkingStyle> markings, ISession session) {
    // Collect all marked functions and their weight by category.
    var markingCategoryList =
      await Task.Run(() => CollectMarkedFunctions(markings, isCategoriesMenu, session));

    CreateCategoriesMenu(markingCategoryList, menu, isCategoriesMenu,
                         menuClickHandler, menuRightClickHandler, session);
  }

  private static void CreateCategoriesMenu(List<FunctionMarkingCategory> temp, MenuItem menu, bool isCategoriesMenu,
                                           MouseButtonEventHandler menuClickHandler,
                                           MouseButtonEventHandler menuRightClickHandler, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    int separatorIndex = !isCategoriesMenu ? defaultItems.FindIndex(item => item is Separator) : -1;
    var markerSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var categoriesValueTemplate = (DataTemplate)Application.Current.
      FindResource("CategoriesProfileMenuItemValueTemplate");
    var checkableValueTemplate = (DataTemplate)Application.Current.
      FindResource("CheckableProfileMenuItemValueTemplate");
    var submenuStyle = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle2");
    var menuStyle = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle");
    double maxWidth = 0;

    foreach (var category in temp) {
      string text = $"({markerSettings.FormatWeightValue(category.Weight)})";
      string title = null;
      string tooltip = null;

      if (isCategoriesMenu) {
        title = category.Marking.TitleOrName;
        tooltip = DocumentUtils.FormatLongFunctionName(category.Marking.Name);
      }
      else {
        tooltip = "Right-click to remove function marking";
        title = category.Marking.Name.TrimToLength(80);

        if (category.Marking.HasTitle && category.Marking.IsRegex) {
          title = $"{category.Marking.Title.TrimToLength(40)} ({title.TrimToLength(40)}) (Regex)";
        }
        else {
          if (category.Marking.HasTitle) {
            title = $"{category.Marking.Title} ({title})";
          }

          if (category.Marking.IsRegex) {
            title = $"{title} (Regex)";
          }
        }
      }

      var value = new ProfileMenuItem(text, category.Weight.Ticks, category.Percentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(category.Percentage),
        TextWeight = markerSettings.PickTextWeight(category.Percentage),
        PercentageBarBackColor = category.HottestFunction != null ?
          markerSettings.PercentageBarBackColor.AsBrush() :
          (Brush)Application.Current.FindResource("ProfileUncategorizedBrush"),
        BackColor = !isCategoriesMenu ? category.Marking.Color.AsBrush() : ColorBrushes.Transparent
      };

      var item = new MenuItem {
        IsChecked = !isCategoriesMenu && category.Marking.IsEnabled,
        StaysOpenOnClick = true,
        Header = value,
        Tag = !isCategoriesMenu ? category.Marking : category.HottestFunction ?? new object(),
        HeaderTemplate = !isCategoriesMenu ? checkableValueTemplate : categoriesValueTemplate,
        Style = category.SortedFunctions is {Count: > 0} ? submenuStyle : menuStyle
      };

      if (menuClickHandler != null) {
        item.PreviewMouseLeftButtonUp += menuClickHandler;
      }

      if (menuRightClickHandler != null) {
        item.PreviewMouseRightButtonDown += menuRightClickHandler;
      }

      item.PreviewMouseLeftButtonDown += (o, args) => {
        if (!isCategoriesMenu && args.Source is MenuItem menuItem &&
            menuItem.Tag is FunctionMarkingStyle) {
          menuItem.IsChecked = !menuItem.IsChecked;
        }
      };

      // Create a submenu with the sorted functions
      // part of the marking/category.
      if (category.SortedFunctions is {Count: > 0}) {
        CreateFunctionsSubmenu(category.SortedFunctions, item, menuClickHandler, session);
      }

      defaultItems.Insert(++separatorIndex, item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var item in profileItems) {
      item.MinTextWidth = maxWidth;
    }

    // Populate the menu.
    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  private static void CreateFunctionsSubmenu(List<ProfileCallTreeNode> functions,
                                             MenuItem parentMenuItem,
                                             MouseButtonEventHandler menuClickHandler,
                                             ISession session) {
    var markerSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var valueTemplate = (DataTemplate)Application.Current.
      FindResource("ProfileMenuItemValueTemplate");
    var menuStyle = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle");
    var profileSubItems = new List<ProfileMenuItem>();
    double subitemMaxWidth = 0;
    int order = 0;

    foreach (var node in functions) {
      double funcWeightPercentage = session.ProfileData.ScaleFunctionWeight(node.Weight);
      double exclFuncWeightPercentage = session.ProfileData.ScaleFunctionWeight(node.ExclusiveWeight);
      string funcText = $"({markerSettings.FormatWeightValue(node.Weight)})";

      // Stop once the weight is too small to be significant.
      if (!markerSettings.IsVisibleValue(order++, funcWeightPercentage)) {
        break;
      }

      string tooltip =
        $"Exclusive Weight: {exclFuncWeightPercentage.AsPercentageString()} ({markerSettings.FormatWeightValue(node.ExclusiveWeight)})\n";
      tooltip +=
        $"Weight: {funcWeightPercentage.AsPercentageString()} ({markerSettings.FormatWeightValue(node.Weight)})\n";
      tooltip += $"Module: {node.ModuleName}";

      if (node is ProfileCallTreeGroupNode groupNode) {
        tooltip += $"\nInstances: {groupNode.Nodes.Count}";
      }

      var funcValue = new ProfileMenuItem(funcText, node.Weight.Ticks, funcWeightPercentage) {
        PrefixText = node.Function.FormatFunctionName(session, 60),
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(funcWeightPercentage),
        TextWeight = markerSettings.PickTextWeight(funcWeightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
      };

      var nodeItem = new MenuItem {
        Header = funcValue,
        Tag = node.Function,
        HeaderTemplate = valueTemplate,
        Style = menuStyle
      };

      if (menuClickHandler != null) {
        nodeItem.PreviewMouseLeftButtonUp += menuClickHandler;
      }

      parentMenuItem.Items.Add(nodeItem);
      profileSubItems.Add(funcValue);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(funcValue.PrefixText, ref subitemMaxWidth, nodeItem);
    }

    foreach (var subItem in profileSubItems) {
      subItem.MinTextWidth = subitemMaxWidth;
    }
  }

  private static void UncheckMenuItems(MenuItem menu, MenuItem excludedItem) {
    foreach (object item in menu.Items) {
      if (item is MenuItem menuItem && menuItem != excludedItem) {
        menuItem.IsChecked = false;
      }
    }
  }

  public static bool ComputeAssemblyWeightInRange(int startLine, int endLine,
                                                  FunctionIR function, FunctionProfileData funcProfile,
                                                  out TimeSpan weightSum, out int count) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();
    bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

    if (!hasInstrOffsetMetadata) {
      weightSum = TimeSpan.Zero;
      count = 0;
      return false;
    }

    weightSum = TimeSpan.Zero;
    count = 0;

    if (startLine > endLine) {
      // Happens when selecting bottom-up.
      (startLine, endLine) = (endLine, startLine);
    }

    foreach (var tuple in function.AllTuples) {
      if (tuple.TextLocation.Line >= startLine &&
          tuple.TextLocation.Line <= endLine) {
        if (metadataTag.ElementToOffsetMap.TryGetValue(tuple, out long offset) &&
            funcProfile.InstructionWeight.TryGetValue(offset, out var weight)) {
          weightSum += weight;
          count++;
        }
      }
    }

    return count > 1;
  }

  public static bool ComputeSourceWeightInRange(int startLine, int endLine,
                                                SourceLineProcessingResult profileResult,
                                                SourceLineProfileResult processingResult,
                                                out TimeSpan weightSum, out int count) {
    weightSum = TimeSpan.Zero;
    count = 0;

    if (startLine > endLine) {
      // Happens when selecting bottom-up.
      (startLine, endLine) = (endLine, startLine);
    }

    for (int i = startLine; i <= endLine; i++) {
      int line = i;

      // With assembly lines, source line numbers are shifted.
      if (processingResult != null) {
        if (processingResult.LineToOriginalLineMap.TryGetValue(line, out int mappedLine)) {
          line = mappedLine;
        }
        else continue;
      }

      if (profileResult.SourceLineWeight.TryGetValue(line, out var weight)) {
        weightSum += weight;
      }

      count++; // Also count lines without weight.
    }

    return count > 1;
  }
}