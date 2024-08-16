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
      HexCheckbox = new ToolStripButton();
      toolStripSeparator2 = new ToolStripSeparator();
      toolStripLabel2 = new ToolStripLabel();
      SearchTextbox = new ToolStripTextBox();
      statusStrip1 = new StatusStrip();
      toolStripStatusLabel1 = new ToolStripStatusLabel();
      SymbolCountLabel = new ToolStripStatusLabel();
      toolStripStatusLabel2 = new ToolStripStatusLabel();
      TotalSymbolCountLabel = new ToolStripStatusLabel();
      StatusLabel = new ToolStripStatusLabel();
      ProgressBar = new ToolStripProgressBar();
      FunctionListView = new ListView();
      columnHeader1 = new ColumnHeader();
      columnHeader2 = new ColumnHeader();
      columnHeader3 = new ColumnHeader();
      columnHeader5 = new ColumnHeader();
      columnHeader4 = new ColumnHeader();
      OpenFileDialog = new OpenFileDialog();
      toolStrip1.SuspendLayout();
      statusStrip1.SuspendLayout();
      SuspendLayout();
      // 
      // toolStrip1
      // 
      toolStrip1.BackColor = SystemColors.Menu;
      toolStrip1.CanOverflow = false;
      toolStrip1.GripStyle = ToolStripGripStyle.Hidden;
      toolStrip1.ImageScalingSize = new Size(28, 28);
      toolStrip1.Items.AddRange(new ToolStripItem[] { OpenButton, toolStripSeparator1, toolStripDropDownButton1, DemangleCheckbox, toolStripSeparator3, toolStripLabel1, RVATextbox, HexCheckbox, toolStripSeparator2, toolStripLabel2, SearchTextbox });
      toolStrip1.Location = new Point(0, 0);
      toolStrip1.Name = "toolStrip1";
      toolStrip1.Size = new Size(1605, 40);
      toolStrip1.TabIndex = 0;
      toolStrip1.Text = "toolStrip1";
      // 
      // OpenButton
      // 
      OpenButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
      OpenButton.Image = (Image)resources.GetObject("OpenButton.Image");
      OpenButton.ImageTransparentColor = Color.Magenta;
      OpenButton.Name = "OpenButton";
      OpenButton.Size = new Size(68, 34);
      OpenButton.Text = "Open";
      OpenButton.Click += OpenButton_Click;
      // 
      // toolStripSeparator1
      // 
      toolStripSeparator1.Name = "toolStripSeparator1";
      toolStripSeparator1.Size = new Size(6, 40);
      // 
      // toolStripDropDownButton1
      // 
      toolStripDropDownButton1.DisplayStyle = ToolStripItemDisplayStyle.Text;
      toolStripDropDownButton1.DropDownItems.AddRange(new ToolStripItem[] { ShowFunctionsCheckbox, ShowPublicsCheckbox });
      toolStripDropDownButton1.Image = (Image)resources.GetObject("toolStripDropDownButton1.Image");
      toolStripDropDownButton1.ImageTransparentColor = Color.Magenta;
      toolStripDropDownButton1.Name = "toolStripDropDownButton1";
      toolStripDropDownButton1.Size = new Size(84, 34);
      toolStripDropDownButton1.Text = "Show";
      // 
      // ShowFunctionsCheckbox
      // 
      ShowFunctionsCheckbox.Checked = true;
      ShowFunctionsCheckbox.CheckOnClick = true;
      ShowFunctionsCheckbox.CheckState = CheckState.Checked;
      ShowFunctionsCheckbox.Name = "ShowFunctionsCheckbox";
      ShowFunctionsCheckbox.Size = new Size(273, 40);
      ShowFunctionsCheckbox.Text = "Show functions";
      ShowFunctionsCheckbox.CheckStateChanged += ShowFunctionsCheckbox_CheckStateChanged;
      // 
      // ShowPublicsCheckbox
      // 
      ShowPublicsCheckbox.Checked = true;
      ShowPublicsCheckbox.CheckOnClick = true;
      ShowPublicsCheckbox.CheckState = CheckState.Checked;
      ShowPublicsCheckbox.Name = "ShowPublicsCheckbox";
      ShowPublicsCheckbox.Size = new Size(273, 40);
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
      DemangleCheckbox.Size = new Size(112, 34);
      DemangleCheckbox.Text = "Demangle";
      DemangleCheckbox.CheckedChanged += DemangleCheckbox_CheckedChanged;
      // 
      // toolStripSeparator3
      // 
      toolStripSeparator3.Name = "toolStripSeparator3";
      toolStripSeparator3.Size = new Size(6, 40);
      // 
      // toolStripLabel1
      // 
      toolStripLabel1.Name = "toolStripLabel1";
      toolStripLabel1.Size = new Size(97, 34);
      toolStripLabel1.Text = "Find RVA";
      // 
      // RVATextbox
      // 
      RVATextbox.Name = "RVATextbox";
      RVATextbox.Size = new Size(150, 40);
      RVATextbox.TextChanged += RVATextbox_TextChanged;
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
      HexCheckbox.Size = new Size(55, 34);
      HexCheckbox.Text = "HEX";
      HexCheckbox.CheckedChanged += HexCheckbox_CheckedChanged;
      // 
      // toolStripSeparator2
      // 
      toolStripSeparator2.Name = "toolStripSeparator2";
      toolStripSeparator2.Size = new Size(6, 40);
      // 
      // toolStripLabel2
      // 
      toolStripLabel2.Name = "toolStripLabel2";
      toolStripLabel2.Size = new Size(133, 34);
      toolStripLabel2.Text = "Search name";
      // 
      // SearchTextbox
      // 
      SearchTextbox.Name = "SearchTextbox";
      SearchTextbox.Size = new Size(200, 40);
      SearchTextbox.TextChanged += SearchTextbox_TextChanged;
      // 
      // statusStrip1
      // 
      statusStrip1.ImageScalingSize = new Size(28, 28);
      statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, SymbolCountLabel, toolStripStatusLabel2, TotalSymbolCountLabel, StatusLabel, ProgressBar });
      statusStrip1.Location = new Point(0, 934);
      statusStrip1.Name = "statusStrip1";
      statusStrip1.Size = new Size(1605, 39);
      statusStrip1.TabIndex = 1;
      statusStrip1.Text = "statusStrip1";
      // 
      // toolStripStatusLabel1
      // 
      toolStripStatusLabel1.Name = "toolStripStatusLabel1";
      toolStripStatusLabel1.Size = new Size(94, 30);
      toolStripStatusLabel1.Text = "Symbols:";
      // 
      // SymbolCountLabel
      // 
      SymbolCountLabel.Name = "SymbolCountLabel";
      SymbolCountLabel.Size = new Size(24, 30);
      SymbolCountLabel.Text = "0";
      // 
      // toolStripStatusLabel2
      // 
      toolStripStatusLabel2.Name = "toolStripStatusLabel2";
      toolStripStatusLabel2.Size = new Size(57, 30);
      toolStripStatusLabel2.Text = "Total";
      // 
      // TotalSymbolCountLabel
      // 
      TotalSymbolCountLabel.Name = "TotalSymbolCountLabel";
      TotalSymbolCountLabel.Size = new Size(24, 30);
      TotalSymbolCountLabel.Text = "0";
      // 
      // StatusLabel
      // 
      StatusLabel.Name = "StatusLabel";
      StatusLabel.Size = new Size(155, 30);
      StatusLabel.Text = "No PDB loaded";
      // 
      // ProgressBar
      // 
      ProgressBar.Name = "ProgressBar";
      ProgressBar.Size = new Size(100, 29);
      ProgressBar.Style = ProgressBarStyle.Marquee;
      ProgressBar.Visible = false;
      // 
      // FunctionListView
      // 
      FunctionListView.Columns.AddRange(new ColumnHeader[] { columnHeader1, columnHeader2, columnHeader3, columnHeader5, columnHeader4 });
      FunctionListView.Dock = DockStyle.Fill;
      FunctionListView.FullRowSelect = true;
      FunctionListView.GridLines = true;
      FunctionListView.Location = new Point(0, 40);
      FunctionListView.MultiSelect = false;
      FunctionListView.Name = "FunctionListView";
      FunctionListView.Size = new Size(1605, 894);
      FunctionListView.TabIndex = 2;
      FunctionListView.UseCompatibleStateImageBehavior = false;
      FunctionListView.View = View.Details;
      // 
      // columnHeader1
      // 
      columnHeader1.Text = "RVA";
      columnHeader1.Width = 120;
      // 
      // columnHeader2
      // 
      columnHeader2.Text = "Name";
      columnHeader2.Width = 600;
      // 
      // columnHeader3
      // 
      columnHeader3.Text = "Length";
      columnHeader3.Width = 100;
      // 
      // columnHeader5
      // 
      columnHeader5.Text = "End RVA";
      columnHeader5.Width = 120;
      // 
      // columnHeader4
      // 
      columnHeader4.Text = "Kind";
      columnHeader4.Width = 120;
      // 
      // OpenFileDialog
      // 
      OpenFileDialog.FileName = "openFileDialog1";
      OpenFileDialog.Filter = "PDB|*.pdb";
      // 
      // MainForm
      // 
      AutoScaleDimensions = new SizeF(12F, 30F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(1605, 973);
      Controls.Add(FunctionListView);
      Controls.Add(statusStrip1);
      Controls.Add(toolStrip1);
      Name = "MainForm";
      StartPosition = FormStartPosition.CenterScreen;
      Text = "PDB Viewer";
      toolStrip1.ResumeLayout(false);
      toolStrip1.PerformLayout();
      statusStrip1.ResumeLayout(false);
      statusStrip1.PerformLayout();
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
    private ListView FunctionListView;
    private ColumnHeader columnHeader1;
    private ColumnHeader columnHeader2;
    private ColumnHeader columnHeader3;
    private ColumnHeader columnHeader4;
    private OpenFileDialog OpenFileDialog;
    private ToolStripButton HexCheckbox;
    private ToolStripStatusLabel toolStripStatusLabel2;
    private ToolStripStatusLabel TotalSymbolCountLabel;
    private ToolStripStatusLabel StatusLabel;
    private ColumnHeader columnHeader5;
    private ToolStripProgressBar ProgressBar;
    private ToolStripButton DemangleCheckbox;
  }
}
