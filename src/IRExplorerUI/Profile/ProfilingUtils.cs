using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Document;
using IRExplorerUI.Profile.Document;

namespace IRExplorerUI.Profile;

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
                                         TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    int order = 0;
    double maxWidth = 0;

    var nodes = session.ProfileData.CallTree.GetSortedCallTreeNodes(section.ParentFunction);
    int maxCallers = nodes.Count >= 2 ? CommonParentCallerIndex(nodes[0], nodes[1]) : Int32.MaxValue;

    foreach (var node in nodes) {
      double weightPercentage = funcProfile.ScaleWeight(node.Weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage) ||
          !node.HasCallers) {
        break;
      }

      var (title, tooltip) = GenerateInstancePreviewText(node, session, maxCallers);
      string text = $"({markerSettings.FormatWeightValue(null, node.Weight)})";

      var value = new ProfileMenuItem(text, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
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

      string text = $"({markerSettings.FormatWeightValue(null, thread.Values.Weight)})";
      string tooltip = threadInfo is {HasName: true} ? threadInfo.Name : null;
      string title = !string.IsNullOrEmpty(tooltip) ? $"{thread.ThreadId} ({tooltip})" : $"{thread.ThreadId}";

      var value = new ProfileMenuItem(text, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
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
    TimeSpan inlineeWeightSum = TimeSpan.Zero;

    foreach (var node in inlineeList) {
      inlineeWeightSum += node.ExclusiveWeight;
    }

    var nonInlineeWeight = funcProfile.Weight - inlineeWeightSum;
    double nonInlineeWeightPercentage = funcProfile.ScaleWeight(nonInlineeWeight);
    string nonInlineeText = $"({markerSettings.FormatWeightValue(null, nonInlineeWeight)})";

    var nonInlineeValue = new ProfileMenuItem(nonInlineeText, nonInlineeWeight.Ticks, nonInlineeWeightPercentage) {
      PrefixText = "Non-Inlinee Code",
      ToolTip = "Time for code not originating from an inlined function",
      ShowPercentageBar = markerSettings.ShowPercentageBar(nonInlineeWeightPercentage),
      TextWeight = markerSettings.PickTextWeight(nonInlineeWeightPercentage),
      PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
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

      var title = node.InlineeFrame.Function.FormatFunctionName(session, 80);
      string text = $"({markerSettings.FormatWeightValue(null, node.ExclusiveWeight)})";
      string tooltip = $"File {Utils.TryGetFileName(node.InlineeFrame.FilePath)}:{node.InlineeFrame.Line}\n";
      tooltip += CreateInlineeFunctionDescription(node, funcProfile, settings.ProfileMarkerSettings, session);

      var value = new ProfileMenuItem(text, node.ExclusiveWeight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
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

  public static void PopulateMarkedModulesMenu(MenuItem menu, FunctionMarkingSettings settings,
                                               ISession session, object triggerObject,
                                               Action changedHandler) {
    if (IsTopLevelSubmenu(triggerObject)) return;

    CreateMarkedModulesMenu(menu,
      (o, args) => {
        if (o is MenuItem menuItem &&
            menuItem.Tag is FunctionMarkingStyle style) {
          style.IsEnabled = menuItem.IsChecked;

          if (style.IsEnabled) {
            settings.UseModuleColors = true;
          }

          changedHandler();
        }
      },
      (o, args) => {
        var style = ((MenuItem)o).Tag as FunctionMarkingStyle;
        settings.ModuleColors.Remove(style);
        menu.IsSubmenuOpen = false;
        changedHandler();
      },
      settings, session);
  }

  public static async Task PopulateMarkedFunctionsMenu(MenuItem menu, FunctionMarkingSettings settings,
                                                       ISession session, object triggerObject,
                                                       Action changedHandler) {
    if (IsTopLevelSubmenu(triggerObject)) return;

    await CreateMarkedFunctionsMenu(menu,
      async (o, args) => {
        if (o is MenuItem menuItem) {
          if (menuItem.Tag is FunctionMarkingStyle style) {
            style.IsEnabled = menuItem.IsChecked;

            if (style.IsEnabled) {
              settings.UseFunctionColors = true;
            }

            changedHandler();
          }
          else if (menuItem.Tag is IRTextFunction func) {
            // Click on submenu with individual functions.
            await session.SwitchActiveFunction(func);
          }
        }
      },
      (o, args) => {
        var style = ((MenuItem)o).Tag as FunctionMarkingStyle;
        settings.FunctionColors.Remove(style);
        menu.IsSubmenuOpen = false;
        changedHandler();
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

  public static void CreateMarkedModulesMenu(MenuItem menu,
                                             RoutedEventHandler menuClickHandler,
                                             MouseButtonEventHandler menuRightClickHandler,
                                             FunctionMarkingSettings settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var separatorIndex = defaultItems.FindIndex(item => item is Separator);
    var markerSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var valueTemplate = (DataTemplate)Application.Current.FindResource("CheckableProfileMenuItemValueTemplate");
    double maxWidth = 0;

    // Sort modules by weight in decreasing order.
    var sortedModules = new List<(FunctionMarkingStyle Module, TimeSpan Weight)>();

    foreach (var moduleStyle in settings.ModuleColors) {
      var moduleWeight = session.ProfileData.FindModulesWeight(name =>
        moduleStyle.NameMatches(name));
      sortedModules.Add((moduleStyle, moduleWeight));
    }

    sortedModules.Sort((a, b) => a.Weight.CompareTo(b.Weight));

    // Insert module markers after separator.
    foreach (var pair in sortedModules) {
      double weightPercentage = session.ProfileData.ScaleModuleWeight(pair.Weight);
      string text = $"({markerSettings.FormatWeightValue(null, pair.Weight)})";
      string tooltip = "Right-click to remove module marking";
      string title = pair.Module.Name;

      if (pair.Module.HasTitle) {
        title = $"{pair.Module.Title} ({title})";
      }

      if (pair.Module.IsRegex) {
        title = $"{title} (Regex)";
      }

      var value = new ProfileMenuItem(text, pair.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
        BackColor = pair.Module.Color.AsBrush()
      };

      var item = new MenuItem {
        IsChecked = pair.Module.IsEnabled,
        IsCheckable = true,
        StaysOpenOnClick = true,
        Header = value,
        Tag = pair.Module,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      item.PreviewMouseRightButtonDown += menuRightClickHandler;
      defaultItems.Insert(separatorIndex + 1, item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    // Populate the module menu.
    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static async Task CreateMarkedFunctionsMenu(MenuItem menu,
                                                     MouseButtonEventHandler menuClickHandler,
                                                     MouseButtonEventHandler menuRightClickHandler,
                                                     FunctionMarkingSettings settings, ISession session) {
    await CreateMarkedFunctionsMenu(menu, false, menuClickHandler, menuRightClickHandler,
      settings.FunctionColors, session, null);
  }

  public static async Task<List<FunctionMarkingCategory>>
    
    CreateFunctionsCategoriesMenu(MenuItem menu,
                                  MouseButtonEventHandler menuClickHandler,
                                  MouseButtonEventHandler menuRightClickHandler,
                                  List<FunctionMarkingCategory> currentMarkingCategories,
                                  FunctionMarkingSettings settings, ISession session) {
    return await CreateMarkedFunctionsMenu(menu, true, menuClickHandler, menuRightClickHandler,
      settings.BuiltinMarkingCategories.FunctionColors, 
      session, currentMarkingCategories);
  }

  public static List<FunctionMarkingCategory> CollectMarkedFunctions(List<FunctionMarkingStyle> markings,
                                                                     bool isCategoriesMenu, ISession session,
                                                                     ProfileCallTreeNode startNode = null) {
    // Collect functions across all markings to compute the "Unmarked" weight.
    var markingCategoryList = new List<FunctionMarkingCategory>();
    var allFuncNodeList = new List<ProfileCallTreeNode>();

    foreach (var marking in markings) {
      // Find all functions matching the marked name. There can be multiple
      // since the same func. name may be used in multiple modules,
      // and also because the name matching may use Regex.
      var funcNodeList = new List<ProfileCallTreeNode>();
      var funcMap = new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();

      if (startNode == null) {
        CollectGlobalMarkedFunctions(marking, funcNodeList, allFuncNodeList, funcMap, session);
      }
      else {
        CollectCallTreeMarkedFunctions(marking, startNode, funcNodeList, allFuncNodeList, funcMap, session);
      }

      // Combine all marked functions to obtain the proper total weight.
      var weight = ProfileCallTree.CombinedCallTreeNodesWeight(funcNodeList);
      double weightPercentage = session.ProfileData.ScaleFunctionWeight(weight);
      var funcList = new List<ProfileCallTreeNode>();
      
      foreach(var pair in funcMap) {
        funcList.Add(ProfileCallTree.CombinedCallTreeNodes(pair.Value));
      }
      
      
      funcList.Sort((a, b) => 
        b.Weight.CompareTo(a.Weight));
      var hottestFunc = funcList.Count > 0 ? funcList[0] : null;
      markingCategoryList.Add(new FunctionMarkingCategory(marking, weight, weightPercentage,
        hottestFunc, funcList));
    }

    // Sort markings by weight in decreasing order.
    markingCategoryList.Sort((a, b) => b.Weight.CompareTo(a.Weight));

    if (isCategoriesMenu) {
      // Compute the "Unmarked" weight and add it as the last entry.
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
                                                     List<ProfileCallTreeNode> allFuncNodeList, 
                                                     Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcNodeMap, ISession session) {
    var nameProvider = session.CompilerInfo.NameProvider;
    var visited = new HashSet<ProfileCallTreeNode>();
    var queue = new Queue<ProfileCallTreeNode>();
    queue.Enqueue(startNode);

    while (queue.Count > 0) {
      var node = queue.Dequeue();

      if (visited.Contains(node)) {
        continue;
      }

      visited.Add(node);

      if (marking.NameMatches(nameProvider.FormatFunctionName(node.Function.Name))) {
        funcNodeList.Add(node); // Per-category list.
        allFuncNodeList.Add(node); // All categories list.
        var instanceNodeList = funcNodeMap.GetOrAddValue(node.Function, () => new List<ProfileCallTreeNode>());
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
                                                   List<ProfileCallTreeNode> allFuncNodeList, 
                                                   Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcNodeMap, ISession session) {
    var nameProvider = session.CompilerInfo.NameProvider;
    
    foreach (var loadedDoc in session.SessionState.Documents) {
      if (loadedDoc.Summary == null) {
        continue;
      }

      var matchingFuncList = loadedDoc.Summary.FindFunctions(name =>
        marking.NameMatches(nameProvider.FormatFunctionName(name)));

      foreach (var func in matchingFuncList) {
        var nodeList = session.ProfileData.CallTree.GetCallTreeNodes(func);

        if (nodeList != null) {
          funcNodeList.AddRange(nodeList); // Per-category list.
          allFuncNodeList.AddRange(nodeList); // All categories list.
        }

        funcNodeMap[func] = nodeList;
      }
    }
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

    while (node != null) {
      // Build the shorter title stack trace.
      if (index < maxCallers && remaining > 0) {
        int maxNameLength = Math.Min(remaining, maxSingleLength);
        var name = node.FormatFunctionName(nameProvider.FormatFunctionName, maxNameLength);
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
        var name = node.FormatFunctionName(nameProvider.FormatFunctionName, maxNameLength);

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
    foreach (var item in menu.Items) {
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

      foreach (var threadId in instanceFilter.ThreadIds) {
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
    var weightPerc = weightFunc(weight);
    var exclusiveWeightPerc = weightFunc(exclusiveWeight);
    var weightText = $"{weightPerc.AsPercentageString()} ({settings.FormatWeightValue(null, weight)})";
    var exclusiveWeightText =
      $"{exclusiveWeightPerc.AsPercentageString()} ({settings.FormatWeightValue(null, exclusiveWeight)})";
    return $"Total time: {weightText}\nSelf time: {exclusiveWeightText}";
  }

  public static void SyncInstancesMenuWithFilter(MenuItem menu, ProfileSampleFilter instanceFilter) {
    foreach (var item in menu.Items) {
      if (item is MenuItem menuItem && menuItem.Tag is ProfileCallTreeNode node) {
        menuItem.IsChecked = instanceFilter != null && instanceFilter.IncludesInstance(node);
      }
    }
  }


  public static async Task<List<FunctionMarkingCategory>>
    CreateMarkedFunctionsMenu(MenuItem menu, bool isCategoriesMenu,
                              MouseButtonEventHandler menuClickHandler,
                              MouseButtonEventHandler menuRightClickHandler,
                              List<FunctionMarkingStyle> markings, ISession session,
                              List<FunctionMarkingCategory> currentMarkingCategories) {
    // Collect all marked functions and their weight by category.
    var markingCategoryList =
      await Task.Run(() => CollectMarkedFunctions(markings, isCategoriesMenu, session));

    if (currentMarkingCategories != null &&
        currentMarkingCategories.Equals(markingCategoryList)) {
      return currentMarkingCategories;
    }

    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var separatorIndex = !isCategoriesMenu ? defaultItems.FindIndex(item => item is Separator) : -1;
    var markerSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var valueTemplate = (DataTemplate)Application.Current.
      FindResource("ProfileMenuItemValueTemplate");
    var categoriesValueTemplate = (DataTemplate)Application.Current.
      FindResource("CategoriesProfileMenuItemValueTemplate");
    var checkableValueTemplate = (DataTemplate)Application.Current.
      FindResource("CheckableProfileMenuItemValueTemplate");
    var submenuStyle = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle2");
    var menuStyle = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle");
    double maxWidth = 0;

    foreach (var category in markingCategoryList) {
      string text = $"({markerSettings.FormatWeightValue(null, category.Weight)})";
      string title = null;
      string tooltip = null;

      if (isCategoriesMenu) {
        title = category.Marking.Title;
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
          (Brush)App.Current.FindResource("ProfileUncategorizedBrush"),
        BackColor = !isCategoriesMenu ? category.Marking.Color.AsBrush() : Brushes.Transparent
      };

      var item = new MenuItem {
        IsChecked = !isCategoriesMenu && category.Marking.IsEnabled,
        StaysOpenOnClick = true,
        Header = value,
        Tag = !isCategoriesMenu ? category.Marking : (category.HottestFunction ?? new object()),
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
        if (!isCategoriesMenu && o is MenuItem menuItem) {
          menuItem.IsChecked = !menuItem.IsChecked;
        }
      };

      // Create a submenu with the sorted functions
      // part of the marking/category.
      if (category.SortedFunctions is {Count: > 0}) {
        var profileSubItems = new List<ProfileMenuItem>();
        double subitemMaxWidth = 0;
        int order = 0;

        foreach (var node in category.SortedFunctions) {
          double funcWeightPercentage = session.ProfileData.ScaleFunctionWeight(node.Weight);
          string funcText = $"({markerSettings.FormatWeightValue(null, node.Weight)})";

          // Stop once the weight is too small to be significant.
          if (!markerSettings.IsVisibleValue(order++, funcWeightPercentage)) {
            break;
          }

          var funcValue = new ProfileMenuItem(funcText, node.Weight.Ticks, funcWeightPercentage) {
            PrefixText = node.Function.FormatFunctionName(session),
            ToolTip = node.Function.ModuleName,
            ShowPercentageBar = markerSettings.ShowPercentageBar(funcWeightPercentage),
            TextWeight = markerSettings.PickTextWeight(funcWeightPercentage),
            PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
          };

          var nodeItem = new MenuItem {
            Header = funcValue,
            Tag = node.Function,
            HeaderTemplate = valueTemplate,
            Style = menuStyle,
          };

          if (menuClickHandler != null) {
            nodeItem.PreviewMouseLeftButtonUp += menuClickHandler;
            nodeItem.PreviewMouseRightButtonUp += menuClickHandler;
          }

          item.Items.Add(nodeItem);
          profileSubItems.Add(funcValue);

          // Make sure percentage rects are aligned.
          Utils.UpdateMaxMenuItemWidth(funcValue.PrefixText, ref subitemMaxWidth, menu);
        }

        foreach (var subItem in profileSubItems) {
          subItem.MinTextWidth = subitemMaxWidth;
        }
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
    return markingCategoryList;
  }

  private static void UncheckMenuItems(MenuItem menu, MenuItem excludedItem) {
    foreach (var item in menu.Items) {
      if (item is MenuItem menuItem && menuItem != excludedItem) {
        menuItem.IsChecked = false;
      }
    }
  }
}