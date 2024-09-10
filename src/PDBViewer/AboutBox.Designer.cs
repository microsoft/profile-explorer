namespace PDBViewer;

partial class AboutBox {
  /// <summary>
  /// Required designer variable.
  /// </summary>
  private System.ComponentModel.IContainer components = null;

  /// <summary>
  /// Clean up any resources being used.
  /// </summary>
  protected override void Dispose(bool disposing) {
    if (disposing && (components != null)) {
      components.Dispose();
    }
    base.Dispose(disposing);
  }

  #region Windows Form Designer generated code

  /// <summary>
  /// Required method for Designer support - do not modify
  /// the contents of this method with the code editor.
  /// </summary>
  private void InitializeComponent() {
    tableLayoutPanel = new TableLayoutPanel();
    labelProductName = new Label();
    labelVersion = new Label();
    labelCopyright = new Label();
    okButton = new Button();
    tableLayoutPanel.SuspendLayout();
    SuspendLayout();
    // 
    // tableLayoutPanel
    // 
    tableLayoutPanel.ColumnCount = 1;
    tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
    tableLayoutPanel.Controls.Add(labelProductName, 0, 0);
    tableLayoutPanel.Controls.Add(labelVersion, 0, 1);
    tableLayoutPanel.Controls.Add(labelCopyright, 0, 2);
    tableLayoutPanel.Controls.Add(okButton, 0, 4);
    tableLayoutPanel.Dock = DockStyle.Fill;
    tableLayoutPanel.Location = new Point(15, 17);
    tableLayoutPanel.Margin = new Padding(5, 6, 5, 6);
    tableLayoutPanel.Name = "tableLayoutPanel";
    tableLayoutPanel.RowCount = 5;
    tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
    tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
    tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
    tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
    tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
    tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
    tableLayoutPanel.Size = new Size(429, 175);
    tableLayoutPanel.TabIndex = 0;
    // 
    // labelProductName
    // 
    labelProductName.Dock = DockStyle.Fill;
    labelProductName.Location = new Point(10, 0);
    labelProductName.Margin = new Padding(10, 0, 5, 0);
    labelProductName.MaximumSize = new Size(0, 33);
    labelProductName.Name = "labelProductName";
    labelProductName.Size = new Size(414, 29);
    labelProductName.TabIndex = 19;
    labelProductName.Text = "Product Name";
    labelProductName.TextAlign = ContentAlignment.MiddleLeft;
    // 
    // labelVersion
    // 
    labelVersion.Dock = DockStyle.Fill;
    labelVersion.Location = new Point(10, 29);
    labelVersion.Margin = new Padding(10, 0, 5, 0);
    labelVersion.MaximumSize = new Size(0, 33);
    labelVersion.Name = "labelVersion";
    labelVersion.Size = new Size(414, 29);
    labelVersion.TabIndex = 0;
    labelVersion.Text = "Version";
    labelVersion.TextAlign = ContentAlignment.MiddleLeft;
    // 
    // labelCopyright
    // 
    labelCopyright.Dock = DockStyle.Fill;
    labelCopyright.Location = new Point(10, 58);
    labelCopyright.Margin = new Padding(10, 0, 5, 0);
    labelCopyright.MaximumSize = new Size(0, 33);
    labelCopyright.Name = "labelCopyright";
    labelCopyright.Size = new Size(414, 29);
    labelCopyright.TabIndex = 21;
    labelCopyright.Text = "Copyright";
    labelCopyright.TextAlign = ContentAlignment.MiddleLeft;
    // 
    // okButton
    // 
    okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
    okButton.DialogResult = DialogResult.Cancel;
    okButton.Location = new Point(322, 137);
    okButton.Margin = new Padding(5, 6, 5, 6);
    okButton.Name = "okButton";
    okButton.Size = new Size(102, 32);
    okButton.TabIndex = 24;
    okButton.Text = "&OK";
    // 
    // AboutBox
    // 
    AcceptButton = okButton;
    AutoScaleDimensions = new SizeF(10F, 25F);
    AutoScaleMode = AutoScaleMode.Font;
    ClientSize = new Size(459, 209);
    Controls.Add(tableLayoutPanel);
    FormBorderStyle = FormBorderStyle.FixedDialog;
    Margin = new Padding(5, 6, 5, 6);
    MaximizeBox = false;
    MinimizeBox = false;
    Name = "AboutBox";
    Padding = new Padding(15, 17, 15, 17);
    ShowIcon = false;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.CenterParent;
    Text = "About PDB Viewer";
    tableLayoutPanel.ResumeLayout(false);
    ResumeLayout(false);
  }

  #endregion

  private System.Windows.Forms.TableLayoutPanel tableLayoutPanel;
  private Label labelProductName;
  private Label labelVersion;
  private Label labelCopyright;
  private Button okButton;
}
