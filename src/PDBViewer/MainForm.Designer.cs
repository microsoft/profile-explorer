namespace PDBViewer
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
      var resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
      toolStrip1 = new ToolStrip();
      OpenButton = new ToolStripButton();
      toolStripSeparator1 = new ToolStripSeparator();
      toolStripDropDownButton1 = new ToolStripDropDownButton();
      ShowFunctionsCheckbox = new ToolStripMenuItem();
      ShowPublicsCheckbox = new ToolStripMenuItem();
      DemangleCheckbox = new ToolStripButton();
      toolStripSeparator3 = new ToolStripSeparator();
      toolStripLabel1 = new ToolStripLabel();
      RVATextbox = new ToolStripTextBox();
      SubtractRVAButton = new ToolStripButton();
      AddRVAButton = new ToolStripButton();
      HexCheckbox = new ToolStripButton();
      toolStripSeparator2 = new ToolStripSeparator();
      toolStripLabel2 = new ToolStripLabel();
      SearchTextbox = new ToolStripTextBox();
      SearchResetButton = new ToolStripButton();
      RegexCheckbox = new ToolStripButton();
      statusStrip1 = new StatusStrip();
      toolStripStatusLabel1 = new ToolStripStatusLabel();
      SymbolCountLabel = new ToolStripStatusLabel();
      toolStripStatusLabel2 = new ToolStripStatusLabel();
      TotalSymbolCountLabel = new ToolStripStatusLabel();
      StatusLabel = new ToolStripStatusLabel();
      ProgressBar = new ToolStripProgressBar();
      OpenFileDialog = new OpenFileDialog();
      splitContainer1 = new SplitContainer();
      FunctionListView = new ListView();
      columnHeader1 = new ColumnHeader();
      columnHeader2 = new ColumnHeader();
      columnHeader3 = new ColumnHeader();
      columnHeader5 = new ColumnHeader();
      columnHeader4 = new ColumnHeader();
      columnHeader6 = new ColumnHeader();
      tabControl1 = new TabControl();
      tabPage1 = new TabPage();
      FunctionDeteailsTextBox = new TextBox();
      tabPage2 = new TabPage();
      splitContainer2 = new SplitContainer();
      SourceLineListView = new ListView();
      columnHeader9 = new ColumnHeader();
      columnHeader7 = new ColumnHeader();
      columnHeader8 = new ColumnHeader();
      columnHeader10 = new ColumnHeader();
      columnHeader14 = new ColumnHeader();
      tabControl2 = new TabControl();
      tabPage3 = new TabPage();
      listView2 = new ListView();
      columnHeader13 = new ColumnHeader();
      columnHeader11 = new ColumnHeader();
      columnHeader12 = new ColumnHeader();
      columnHeader15 = new ColumnHeader();
      tabPage4 = new TabPage();
      toolStrip2 = new ToolStrip();
      toolStripButton1 = new ToolStripButton();
      toolStripSeparator5 = new ToolStripSeparator();
      toolStripButton2 = new ToolStripButton();
      toolStripButton3 = new ToolStripButton();
      toolStripSeparator6 = new ToolStripSeparator();
      toolStripLabel3 = new ToolStripLabel();
      richTextBox1 = new RichTextBox();
      toolStrip1.SuspendLayout();
      statusStrip1.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
      splitContainer1.Panel1.SuspendLayout();
      splitContainer1.Panel2.SuspendLayout();
      splitContainer1.SuspendLayout();
      tabControl1.SuspendLayout();
      tabPage1.SuspendLayout();
      tabPage2.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
      splitContainer2.Panel1.SuspendLayout();
      splitContainer2.Panel2.SuspendLayout();
      splitContainer2.SuspendLayout();
      tabControl2.SuspendLayout();
      tabPage3.SuspendLayout();
      tabPage4.SuspendLayout();
      toolStrip2.SuspendLayout();
      SuspendLayout();
      // 
      // toolStrip1
      // 
      toolStrip1.AutoSize = false;
      toolStrip1.BackColor = SystemColors.Control;
      toolStrip1.CanOverflow = false;
      toolStrip1.GripStyle = ToolStripGripStyle.Hidden;
      toolStrip1.ImageScalingSize = new Size(28, 28);
      toolStrip1.Items.AddRange(new ToolStripItem[] { OpenButton, toolStripSeparator1, toolStripDropDownButton1, DemangleCheckbox, toolStripSeparator3, toolStripLabel1, RVATextbox, SubtractRVAButton, AddRVAButton, HexCheckbox, toolStripSeparator2, toolStripLabel2, SearchTextbox, SearchResetButton, RegexCheckbox });
      toolStrip1.Location = new Point(0, 0);
      toolStrip1.Name = "toolStrip1";
      toolStrip1.Padding = new Padding(0, 0, 2, 0);
      toolStrip1.RenderMode = ToolStripRenderMode.System;
      toolStrip1.Size = new Size(1253, 32);
      toolStrip1.TabIndex = 0;
      toolStrip1.Text = "toolStrip1";
      // 
      // OpenButton
      // 
      OpenButton.Image = (Image)resources.GetObject("OpenButton.Image");
      OpenButton.ImageScaling = ToolStripItemImageScaling.None;
      OpenButton.ImageTransparentColor = Color.Magenta;
      OpenButton.Margin = new Padding(2, 1, 0, 2);
      OpenButton.Name = "OpenButton";
      OpenButton.Size = new Size(65, 29);
      OpenButton.Text = "Open";
      OpenButton.ToolTipText = "Open PDB file";
      OpenButton.Click += OpenButton_Click;
      // 
      // toolStripSeparator1
      // 
      toolStripSeparator1.Name = "toolStripSeparator1";
      toolStripSeparator1.Size = new Size(6, 32);
      // 
      // toolStripDropDownButton1
      // 
      toolStripDropDownButton1.DisplayStyle = ToolStripItemDisplayStyle.Text;
      toolStripDropDownButton1.DropDownItems.AddRange(new ToolStripItem[] { ShowFunctionsCheckbox, ShowPublicsCheckbox });
      toolStripDropDownButton1.Image = (Image)resources.GetObject("toolStripDropDownButton1.Image");
      toolStripDropDownButton1.ImageTransparentColor = Color.Magenta;
      toolStripDropDownButton1.Name = "toolStripDropDownButton1";
      toolStripDropDownButton1.Size = new Size(59, 29);
      toolStripDropDownButton1.Text = "Show";
      // 
      // ShowFunctionsCheckbox
      // 
      ShowFunctionsCheckbox.Checked = true;
      ShowFunctionsCheckbox.CheckOnClick = true;
      ShowFunctionsCheckbox.CheckState = CheckState.Checked;
      ShowFunctionsCheckbox.Name = "ShowFunctionsCheckbox";
      ShowFunctionsCheckbox.Size = new Size(192, 26);
      ShowFunctionsCheckbox.Text = "Show functions";
      ShowFunctionsCheckbox.CheckStateChanged += ShowFunctionsCheckbox_CheckStateChanged;
      // 
      // ShowPublicsCheckbox
      // 
      ShowPublicsCheckbox.Checked = true;
      ShowPublicsCheckbox.CheckOnClick = true;
      ShowPublicsCheckbox.CheckState = CheckState.Checked;
      ShowPublicsCheckbox.Name = "ShowPublicsCheckbox";
      ShowPublicsCheckbox.Size = new Size(192, 26);
      ShowPublicsCheckbox.Text = "Show publics";
      ShowPublicsCheckbox.CheckedChanged += ShowPublicsCheckbox_CheckedChanged;
      // 
      // DemangleCheckbox
      // 
      DemangleCheckbox.Checked = true;
      DemangleCheckbox.CheckOnClick = true;
      DemangleCheckbox.CheckState = CheckState.Checked;
      DemangleCheckbox.DisplayStyle = ToolStripItemDisplayStyle.Text;
      DemangleCheckbox.Image = (Image)resources.GetObject("DemangleCheckbox.Image");
      DemangleCheckbox.ImageTransparentColor = Color.Magenta;
      DemangleCheckbox.Name = "DemangleCheckbox";
      DemangleCheckbox.Size = new Size(82, 29);
      DemangleCheckbox.Text = "Demangle";
      DemangleCheckbox.ToolTipText = "Demangle C++ function names";
      DemangleCheckbox.CheckedChanged += DemangleCheckbox_CheckedChanged;
      // 
      // toolStripSeparator3
      // 
      toolStripSeparator3.Name = "toolStripSeparator3";
      toolStripSeparator3.Size = new Size(6, 32);
      // 
      // toolStripLabel1
      // 
      toolStripLabel1.Image = (Image)resources.GetObject("toolStripLabel1.Image");
      toolStripLabel1.ImageScaling = ToolStripItemImageScaling.None;
      toolStripLabel1.Margin = new Padding(2, 2, 0, 4);
      toolStripLabel1.Name = "toolStripLabel1";
      toolStripLabel1.Size = new Size(52, 26);
      toolStripLabel1.Text = "RVA";
      // 
      // RVATextbox
      // 
      RVATextbox.Name = "RVATextbox";
      RVATextbox.Size = new Size(101, 32);
      RVATextbox.ToolTipText = "Search for functions with an RVA in range";
      RVATextbox.TextChanged += RVATextbox_TextChanged;
      // 
      // SubtractRVAButton
      // 
      SubtractRVAButton.BackgroundImageLayout = ImageLayout.None;
      SubtractRVAButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
      SubtractRVAButton.Image = (Image)resources.GetObject("SubtractRVAButton.Image");
      SubtractRVAButton.ImageScaling = ToolStripItemImageScaling.None;
      SubtractRVAButton.ImageTransparentColor = Color.Magenta;
      SubtractRVAButton.Name = "SubtractRVAButton";
      SubtractRVAButton.Size = new Size(29, 29);
      SubtractRVAButton.Text = "toolStripButton1";
      SubtractRVAButton.ToolTipText = "Decrement searched RVA";
      SubtractRVAButton.Click += SubtractRVAButton_Click;
      // 
      // AddRVAButton
      // 
      AddRVAButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
      AddRVAButton.Image = (Image)resources.GetObject("AddRVAButton.Image");
      AddRVAButton.ImageScaling = ToolStripItemImageScaling.None;
      AddRVAButton.ImageTransparentColor = Color.Magenta;
      AddRVAButton.Name = "AddRVAButton";
      AddRVAButton.Size = new Size(29, 29);
      AddRVAButton.Text = "toolStripButton1";
      AddRVAButton.ToolTipText = "Increment searched RVA";
      AddRVAButton.Click += AddRVAButton_Click;
      // 
      // HexCheckbox
      // 
      HexCheckbox.Checked = true;
      HexCheckbox.CheckOnClick = true;
      HexCheckbox.CheckState = CheckState.Checked;
      HexCheckbox.DisplayStyle = ToolStripItemDisplayStyle.Text;
      HexCheckbox.Image = (Image)resources.GetObject("HexCheckbox.Image");
      HexCheckbox.ImageTransparentColor = Color.Magenta;
      HexCheckbox.Name = "HexCheckbox";
      HexCheckbox.Size = new Size(41, 29);
      HexCheckbox.Text = "HEX";
      HexCheckbox.ToolTipText = "Handle RVA value as hexadecimal";
      HexCheckbox.CheckedChanged += HexCheckbox_CheckedChanged;
      // 
      // toolStripSeparator2
      // 
      toolStripSeparator2.Name = "toolStripSeparator2";
      toolStripSeparator2.Size = new Size(6, 32);
      // 
      // toolStripLabel2
      // 
      toolStripLabel2.Image = (Image)resources.GetObject("toolStripLabel2.Image");
      toolStripLabel2.ImageScaling = ToolStripItemImageScaling.None;
      toolStripLabel2.Margin = new Padding(2, 2, 0, 4);
      toolStripLabel2.Name = "toolStripLabel2";
      toolStripLabel2.Size = new Size(99, 26);
      toolStripLabel2.Text = "Filter name";
      // 
      // SearchTextbox
      // 
      SearchTextbox.Name = "SearchTextbox";
      SearchTextbox.Size = new Size(200, 32);
      SearchTextbox.ToolTipText = "Filter list to functions containing substring";
      SearchTextbox.TextChanged += SearchTextbox_TextChanged;
      // 
      // SearchResetButton
      // 
      SearchResetButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
      SearchResetButton.Image = (Image)resources.GetObject("SearchResetButton.Image");
      SearchResetButton.ImageScaling = ToolStripItemImageScaling.None;
      SearchResetButton.ImageTransparentColor = Color.Magenta;
      SearchResetButton.Name = "SearchResetButton";
      SearchResetButton.Size = new Size(29, 29);
      SearchResetButton.Text = "toolStripButton1";
      SearchResetButton.Click += SearchResetButton_Click;
      // 
      // RegexCheckbox
      // 
      RegexCheckbox.CheckOnClick = true;
      RegexCheckbox.DisplayStyle = ToolStripItemDisplayStyle.Text;
      RegexCheckbox.Enabled = false;
      RegexCheckbox.Image = (Image)resources.GetObject("RegexCheckbox.Image");
      RegexCheckbox.ImageTransparentColor = Color.Magenta;
      RegexCheckbox.Name = "RegexCheckbox";
      RegexCheckbox.Size = new Size(29, 29);
      RegexCheckbox.Text = "R*";
      RegexCheckbox.ToolTipText = "Use regex function name filter";
      // 
      // statusStrip1
      // 
      statusStrip1.ImageScalingSize = new Size(28, 28);
      statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, SymbolCountLabel, toolStripStatusLabel2, TotalSymbolCountLabel, StatusLabel, ProgressBar });
      statusStrip1.Location = new Point(0, 775);
      statusStrip1.Name = "statusStrip1";
      statusStrip1.Padding = new Padding(1, 0, 9, 0);
      statusStrip1.Size = new Size(1253, 26);
      statusStrip1.TabIndex = 1;
      statusStrip1.Text = "statusStrip1";
      // 
      // toolStripStatusLabel1
      // 
      toolStripStatusLabel1.Name = "toolStripStatusLabel1";
      toolStripStatusLabel1.Size = new Size(68, 20);
      toolStripStatusLabel1.Text = "Symbols:";
      // 
      // SymbolCountLabel
      // 
      SymbolCountLabel.Name = "SymbolCountLabel";
      SymbolCountLabel.Size = new Size(17, 20);
      SymbolCountLabel.Text = "0";
      // 
      // toolStripStatusLabel2
      // 
      toolStripStatusLabel2.Name = "toolStripStatusLabel2";
      toolStripStatusLabel2.Size = new Size(42, 20);
      toolStripStatusLabel2.Text = "Total";
      // 
      // TotalSymbolCountLabel
      // 
      TotalSymbolCountLabel.Name = "TotalSymbolCountLabel";
      TotalSymbolCountLabel.Size = new Size(17, 20);
      TotalSymbolCountLabel.Text = "0";
      // 
      // StatusLabel
      // 
      StatusLabel.Name = "StatusLabel";
      StatusLabel.Size = new Size(112, 20);
      StatusLabel.Text = "No PDB loaded";
      // 
      // ProgressBar
      // 
      ProgressBar.Name = "ProgressBar";
      ProgressBar.Size = new Size(67, 21);
      ProgressBar.Style = ProgressBarStyle.Marquee;
      ProgressBar.Visible = false;
      // 
      // OpenFileDialog
      // 
      OpenFileDialog.FileName = "openFileDialog1";
      OpenFileDialog.Filter = "PDB|*.pdb";
      // 
      // splitContainer1
      // 
      splitContainer1.Dock = DockStyle.Fill;
      splitContainer1.Location = new Point(0, 32);
      splitContainer1.Margin = new Padding(2);
      splitContainer1.Name = "splitContainer1";
      splitContainer1.Orientation = Orientation.Horizontal;
      // 
      // splitContainer1.Panel1
      // 
      splitContainer1.Panel1.Controls.Add(FunctionListView);
      // 
      // splitContainer1.Panel2
      // 
      splitContainer1.Panel2.Controls.Add(tabControl1);
      splitContainer1.Size = new Size(1253, 743);
      splitContainer1.SplitterDistance = 424;
      splitContainer1.SplitterWidth = 2;
      splitContainer1.TabIndex = 2;
      // 
      // FunctionListView
      // 
      FunctionListView.Columns.AddRange(new ColumnHeader[] { columnHeader1, columnHeader2, columnHeader3, columnHeader5, columnHeader4, columnHeader6 });
      FunctionListView.Dock = DockStyle.Fill;
      FunctionListView.FullRowSelect = true;
      FunctionListView.GridLines = true;
      FunctionListView.Location = new Point(0, 0);
      FunctionListView.Margin = new Padding(2);
      FunctionListView.MultiSelect = false;
      FunctionListView.Name = "FunctionListView";
      FunctionListView.Size = new Size(1253, 424);
      FunctionListView.TabIndex = 3;
      FunctionListView.UseCompatibleStateImageBehavior = false;
      FunctionListView.View = View.Details;
      FunctionListView.SelectedIndexChanged += FunctionListView_SelectedIndexChanged;
      // 
      // columnHeader1
      // 
      columnHeader1.Text = "RVA";
      columnHeader1.Width = 100;
      // 
      // columnHeader2
      // 
      columnHeader2.Text = "Name";
      columnHeader2.Width = 600;
      // 
      // columnHeader3
      // 
      columnHeader3.Text = "Length";
      columnHeader3.Width = 120;
      // 
      // columnHeader5
      // 
      columnHeader5.Text = "End RVA";
      columnHeader5.Width = 100;
      // 
      // columnHeader4
      // 
      columnHeader4.Text = "Kind";
      columnHeader4.Width = 120;
      // 
      // columnHeader6
      // 
      columnHeader6.Text = "Mangled name";
      columnHeader6.Width = 180;
      // 
      // tabControl1
      // 
      tabControl1.Controls.Add(tabPage1);
      tabControl1.Controls.Add(tabPage2);
      tabControl1.Dock = DockStyle.Fill;
      tabControl1.Location = new Point(0, 0);
      tabControl1.Margin = new Padding(2);
      tabControl1.Name = "tabControl1";
      tabControl1.SelectedIndex = 0;
      tabControl1.Size = new Size(1253, 317);
      tabControl1.TabIndex = 0;
      // 
      // tabPage1
      // 
      tabPage1.Controls.Add(FunctionDeteailsTextBox);
      tabPage1.Location = new Point(4, 29);
      tabPage1.Margin = new Padding(2);
      tabPage1.Name = "tabPage1";
      tabPage1.Padding = new Padding(2);
      tabPage1.Size = new Size(1245, 284);
      tabPage1.TabIndex = 0;
      tabPage1.Text = "Details";
      tabPage1.UseVisualStyleBackColor = true;
      // 
      // FunctionDeteailsTextBox
      // 
      FunctionDeteailsTextBox.BackColor = SystemColors.Control;
      FunctionDeteailsTextBox.BorderStyle = BorderStyle.None;
      FunctionDeteailsTextBox.Dock = DockStyle.Fill;
      FunctionDeteailsTextBox.HideSelection = false;
      FunctionDeteailsTextBox.Location = new Point(2, 2);
      FunctionDeteailsTextBox.Margin = new Padding(2);
      FunctionDeteailsTextBox.Multiline = true;
      FunctionDeteailsTextBox.Name = "FunctionDeteailsTextBox";
      FunctionDeteailsTextBox.PlaceholderText = "Selected function information";
      FunctionDeteailsTextBox.ReadOnly = true;
      FunctionDeteailsTextBox.Size = new Size(1241, 280);
      FunctionDeteailsTextBox.TabIndex = 0;
      // 
      // tabPage2
      // 
      tabPage2.Controls.Add(splitContainer2);
      tabPage2.Location = new Point(4, 29);
      tabPage2.Margin = new Padding(2);
      tabPage2.Name = "tabPage2";
      tabPage2.Padding = new Padding(2);
      tabPage2.Size = new Size(1245, 284);
      tabPage2.TabIndex = 1;
      tabPage2.Text = "Source Lines";
      tabPage2.UseVisualStyleBackColor = true;
      // 
      // splitContainer2
      // 
      splitContainer2.Dock = DockStyle.Fill;
      splitContainer2.Location = new Point(2, 2);
      splitContainer2.Name = "splitContainer2";
      // 
      // splitContainer2.Panel1
      // 
      splitContainer2.Panel1.Controls.Add(SourceLineListView);
      // 
      // splitContainer2.Panel2
      // 
      splitContainer2.Panel2.Controls.Add(tabControl2);
      splitContainer2.Size = new Size(1241, 280);
      splitContainer2.SplitterDistance = 481;
      splitContainer2.TabIndex = 0;
      // 
      // SourceLineListView
      // 
      SourceLineListView.Columns.AddRange(new ColumnHeader[] { columnHeader9, columnHeader7, columnHeader8, columnHeader10, columnHeader14 });
      SourceLineListView.Dock = DockStyle.Fill;
      SourceLineListView.FullRowSelect = true;
      SourceLineListView.GridLines = true;
      SourceLineListView.Location = new Point(0, 0);
      SourceLineListView.Margin = new Padding(2);
      SourceLineListView.MultiSelect = false;
      SourceLineListView.Name = "SourceLineListView";
      SourceLineListView.Size = new Size(481, 280);
      SourceLineListView.TabIndex = 4;
      SourceLineListView.UseCompatibleStateImageBehavior = false;
      SourceLineListView.View = View.Details;
      // 
      // columnHeader9
      // 
      columnHeader9.Text = "RVA";
      columnHeader9.Width = 80;
      // 
      // columnHeader7
      // 
      columnHeader7.Text = "Line";
      columnHeader7.Width = 80;
      // 
      // columnHeader8
      // 
      columnHeader8.Text = "Column";
      columnHeader8.Width = 80;
      // 
      // columnHeader10
      // 
      columnHeader10.Text = "Inlinees";
      columnHeader10.Width = 80;
      // 
      // columnHeader14
      // 
      columnHeader14.Text = "File";
      columnHeader14.Width = 100;
      // 
      // tabControl2
      // 
      tabControl2.Controls.Add(tabPage3);
      tabControl2.Controls.Add(tabPage4);
      tabControl2.Dock = DockStyle.Fill;
      tabControl2.Location = new Point(0, 0);
      tabControl2.Name = "tabControl2";
      tabControl2.SelectedIndex = 0;
      tabControl2.Size = new Size(756, 280);
      tabControl2.TabIndex = 0;
      // 
      // tabPage3
      // 
      tabPage3.Controls.Add(listView2);
      tabPage3.Location = new Point(4, 29);
      tabPage3.Name = "tabPage3";
      tabPage3.Padding = new Padding(3);
      tabPage3.Size = new Size(748, 247);
      tabPage3.TabIndex = 0;
      tabPage3.Text = "Inlinees";
      tabPage3.UseVisualStyleBackColor = true;
      // 
      // listView2
      // 
      listView2.Columns.AddRange(new ColumnHeader[] { columnHeader13, columnHeader11, columnHeader12, columnHeader15 });
      listView2.Dock = DockStyle.Fill;
      listView2.FullRowSelect = true;
      listView2.GridLines = true;
      listView2.Location = new Point(3, 3);
      listView2.Margin = new Padding(2);
      listView2.MultiSelect = false;
      listView2.Name = "listView2";
      listView2.Size = new Size(742, 241);
      listView2.TabIndex = 6;
      listView2.UseCompatibleStateImageBehavior = false;
      listView2.View = View.Details;
      // 
      // columnHeader13
      // 
      columnHeader13.Text = "Inlinee Name";
      columnHeader13.Width = 250;
      // 
      // columnHeader11
      // 
      columnHeader11.Text = "Line";
      columnHeader11.Width = 80;
      // 
      // columnHeader12
      // 
      columnHeader12.Text = "Column";
      columnHeader12.Width = 80;
      // 
      // columnHeader15
      // 
      columnHeader15.Text = "File Name";
      columnHeader15.Width = 200;
      // 
      // tabPage4
      // 
      tabPage4.Controls.Add(toolStrip2);
      tabPage4.Controls.Add(richTextBox1);
      tabPage4.Location = new Point(4, 29);
      tabPage4.Name = "tabPage4";
      tabPage4.Padding = new Padding(3);
      tabPage4.Size = new Size(748, 247);
      tabPage4.TabIndex = 1;
      tabPage4.Text = "Source Preview";
      tabPage4.UseVisualStyleBackColor = true;
      // 
      // toolStrip2
      // 
      toolStrip2.BackColor = SystemColors.Control;
      toolStrip2.GripStyle = ToolStripGripStyle.Hidden;
      toolStrip2.ImageScalingSize = new Size(20, 20);
      toolStrip2.Items.AddRange(new ToolStripItem[] { toolStripButton1, toolStripSeparator5, toolStripButton2, toolStripButton3, toolStripSeparator6, toolStripLabel3 });
      toolStrip2.Location = new Point(3, 3);
      toolStrip2.Name = "toolStrip2";
      toolStrip2.Size = new Size(742, 27);
      toolStrip2.TabIndex = 1;
      toolStrip2.Text = "toolStrip2";
      // 
      // toolStripButton1
      // 
      toolStripButton1.Image = (Image)resources.GetObject("toolStripButton1.Image");
      toolStripButton1.ImageScaling = ToolStripItemImageScaling.None;
      toolStripButton1.ImageTransparentColor = Color.Magenta;
      toolStripButton1.Name = "toolStripButton1";
      toolStripButton1.Size = new Size(65, 24);
      toolStripButton1.Text = "Open";
      // 
      // toolStripSeparator5
      // 
      toolStripSeparator5.Name = "toolStripSeparator5";
      toolStripSeparator5.Size = new Size(6, 27);
      // 
      // toolStripButton2
      // 
      toolStripButton2.DisplayStyle = ToolStripItemDisplayStyle.Image;
      toolStripButton2.Image = (Image)resources.GetObject("toolStripButton2.Image");
      toolStripButton2.ImageScaling = ToolStripItemImageScaling.None;
      toolStripButton2.ImageTransparentColor = Color.Magenta;
      toolStripButton2.Name = "toolStripButton2";
      toolStripButton2.Size = new Size(29, 24);
      toolStripButton2.Text = "toolStripButton2";
      // 
      // toolStripButton3
      // 
      toolStripButton3.DisplayStyle = ToolStripItemDisplayStyle.Image;
      toolStripButton3.Image = (Image)resources.GetObject("toolStripButton3.Image");
      toolStripButton3.ImageScaling = ToolStripItemImageScaling.None;
      toolStripButton3.ImageTransparentColor = Color.Magenta;
      toolStripButton3.Name = "toolStripButton3";
      toolStripButton3.Size = new Size(29, 24);
      toolStripButton3.Text = "toolStripButton3";
      // 
      // toolStripSeparator6
      // 
      toolStripSeparator6.Name = "toolStripSeparator6";
      toolStripSeparator6.Size = new Size(6, 27);
      // 
      // toolStripLabel3
      // 
      toolStripLabel3.Name = "toolStripLabel3";
      toolStripLabel3.Size = new Size(152, 24);
      toolStripLabel3.Text = "No source file loaded";
      // 
      // richTextBox1
      // 
      richTextBox1.BorderStyle = BorderStyle.None;
      richTextBox1.Dock = DockStyle.Bottom;
      richTextBox1.Font = new Font("Consolas", 10.2F, FontStyle.Regular, GraphicsUnit.Point, 0);
      richTextBox1.Location = new Point(3, 26);
      richTextBox1.Name = "richTextBox1";
      richTextBox1.Size = new Size(742, 218);
      richTextBox1.TabIndex = 0;
      richTextBox1.Text = "Open source file";
      richTextBox1.WordWrap = false;
      // 
      // MainForm
      // 
      AutoScaleDimensions = new SizeF(8F, 20F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(1253, 801);
      Controls.Add(splitContainer1);
      Controls.Add(statusStrip1);
      Controls.Add(toolStrip1);
      Margin = new Padding(2);
      Name = "MainForm";
      StartPosition = FormStartPosition.CenterScreen;
      Text = "PDB Viewer";
      toolStrip1.ResumeLayout(false);
      toolStrip1.PerformLayout();
      statusStrip1.ResumeLayout(false);
      statusStrip1.PerformLayout();
      splitContainer1.Panel1.ResumeLayout(false);
      splitContainer1.Panel2.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
      splitContainer1.ResumeLayout(false);
      tabControl1.ResumeLayout(false);
      tabPage1.ResumeLayout(false);
      tabPage1.PerformLayout();
      tabPage2.ResumeLayout(false);
      splitContainer2.Panel1.ResumeLayout(false);
      splitContainer2.Panel2.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
      splitContainer2.ResumeLayout(false);
      tabControl2.ResumeLayout(false);
      tabPage3.ResumeLayout(false);
      tabPage4.ResumeLayout(false);
      tabPage4.PerformLayout();
      toolStrip2.ResumeLayout(false);
      toolStrip2.PerformLayout();
      ResumeLayout(false);
      PerformLayout();
    }

    #endregion

    private ToolStrip toolStrip1;
    private ToolStripButton OpenButton;
    private ToolStripSeparator toolStripSeparator1;
    private ToolStripLabel toolStripLabel1;
    private StatusStrip statusStrip1;
    private ToolStripStatusLabel toolStripStatusLabel1;
    private ToolStripDropDownButton toolStripDropDownButton1;
    private ToolStripMenuItem ShowFunctionsCheckbox;
    private ToolStripMenuItem ShowPublicsCheckbox;
    private ToolStripSeparator toolStripSeparator3;
    private ToolStripTextBox RVATextbox;
    private ToolStripSeparator toolStripSeparator2;
    private ToolStripLabel toolStripLabel2;
    private ToolStripTextBox SearchTextbox;
    private ToolStripStatusLabel SymbolCountLabel;
    private OpenFileDialog OpenFileDialog;
    private ToolStripButton HexCheckbox;
    private ToolStripStatusLabel toolStripStatusLabel2;
    private ToolStripStatusLabel TotalSymbolCountLabel;
    private ToolStripStatusLabel StatusLabel;
    private ToolStripProgressBar ProgressBar;
    private ToolStripButton DemangleCheckbox;
    private ToolStripButton AddRVAButton;
    private ToolStripButton SubtractRVAButton;
    private ToolStripButton SearchResetButton;
    private SplitContainer splitContainer1;
    private ListView FunctionListView;
    private ColumnHeader columnHeader1;
    private ColumnHeader columnHeader2;
    private ColumnHeader columnHeader3;
    private ColumnHeader columnHeader5;
    private ColumnHeader columnHeader4;
    private ColumnHeader columnHeader6;
    private TabControl tabControl1;
    private TabPage tabPage1;
    private TabPage tabPage2;
    private TextBox FunctionDeteailsTextBox;
    private SplitContainer splitContainer2;
    private ListView SourceLineListView;
    private ColumnHeader columnHeader7;
    private ColumnHeader columnHeader8;
    private ColumnHeader columnHeader9;
    private ColumnHeader columnHeader10;
    private TabControl tabControl2;
    private TabPage tabPage3;
    private TabPage tabPage4;
    private ListView listView2;
    private ColumnHeader columnHeader13;
    private ColumnHeader columnHeader11;
    private ColumnHeader columnHeader12;
    private ColumnHeader columnHeader15;
    private RichTextBox richTextBox1;
    private ToolStripButton RegexCheckbox;
    private ToolStrip toolStrip2;
    private ToolStripButton toolStripButton1;
    private ToolStripSeparator toolStripSeparator5;
    private ToolStripButton toolStripButton2;
    private ToolStripButton toolStripButton3;
    private ToolStripSeparator toolStripSeparator6;
    private ToolStripLabel toolStripLabel3;
    private ColumnHeader columnHeader14;
  }
}
