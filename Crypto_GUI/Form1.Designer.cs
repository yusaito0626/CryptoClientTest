namespace Crypto_GUI
{
    partial class Form1
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
        private void InitializeComponent()
        {
            DataGridViewCellStyle dataGridViewCellStyle1 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle2 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle3 = new DataGridViewCellStyle();
            button1 = new Button();
            tabControl = new TabControl();
            tabPage1 = new TabPage();
            textBoxMainLog = new RichTextBox();
            button2 = new Button();
            tabPage3 = new TabPage();
            lbl_takerName = new Label();
            lbl_makerName = new Label();
            gridView_Maker = new DataGridView();
            dataGridViewTextBoxColumn1 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn2 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn3 = new DataGridViewTextBoxColumn();
            gridView_Taker = new DataGridView();
            col_Ask = new DataGridViewTextBoxColumn();
            col_price = new DataGridViewTextBoxColumn();
            col_Bid = new DataGridViewTextBoxColumn();
            tabPage2 = new TabPage();
            gridView_Ins = new DataGridView();
            dataGridViewTextBoxColumn4 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn5 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn6 = new DataGridViewTextBoxColumn();
            lbl_notional = new Label();
            lbl_lastprice = new Label();
            lbl_market = new Label();
            lbl_symbol = new Label();
            label4 = new Label();
            label3 = new Label();
            label2 = new Label();
            label1 = new Label();
            comboSymbols = new ComboBox();
            tabControl.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)gridView_Maker).BeginInit();
            ((System.ComponentModel.ISupportInitialize)gridView_Taker).BeginInit();
            tabPage2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)gridView_Ins).BeginInit();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Location = new Point(1125, 53);
            button1.Name = "button1";
            button1.Size = new Size(343, 78);
            button1.TabIndex = 0;
            button1.Text = "ReceiveFeed";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabPage1);
            tabControl.Controls.Add(tabPage3);
            tabControl.Controls.Add(tabPage2);
            tabControl.Location = new Point(12, 12);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(1555, 950);
            tabControl.TabIndex = 4;
            // 
            // tabPage1
            // 
            tabPage1.BackColor = Color.WhiteSmoke;
            tabPage1.Controls.Add(textBoxMainLog);
            tabPage1.Controls.Add(button2);
            tabPage1.Controls.Add(button1);
            tabPage1.Location = new Point(8, 46);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1539, 896);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Main";
            // 
            // textBoxMainLog
            // 
            textBoxMainLog.Font = new Font("Calibri", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            textBoxMainLog.Location = new Point(6, 272);
            textBoxMainLog.Name = "textBoxMainLog";
            textBoxMainLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            textBoxMainLog.Size = new Size(1527, 618);
            textBoxMainLog.TabIndex = 3;
            textBoxMainLog.Text = "";
            // 
            // button2
            // 
            button2.Location = new Point(1125, 159);
            button2.Name = "button2";
            button2.Size = new Size(343, 78);
            button2.TabIndex = 2;
            button2.Text = "Test Order";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // tabPage3
            // 
            tabPage3.BackColor = Color.WhiteSmoke;
            tabPage3.Controls.Add(lbl_takerName);
            tabPage3.Controls.Add(lbl_makerName);
            tabPage3.Controls.Add(gridView_Maker);
            tabPage3.Controls.Add(gridView_Taker);
            tabPage3.Location = new Point(8, 46);
            tabPage3.Name = "tabPage3";
            tabPage3.Padding = new Padding(3);
            tabPage3.Size = new Size(1539, 896);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "Strategy";
            // 
            // lbl_takerName
            // 
            lbl_takerName.AutoSize = true;
            lbl_takerName.Location = new Point(809, 61);
            lbl_takerName.Name = "lbl_takerName";
            lbl_takerName.Size = new Size(67, 32);
            lbl_takerName.TabIndex = 4;
            lbl_takerName.Text = "taker";
            // 
            // lbl_makerName
            // 
            lbl_makerName.AutoSize = true;
            lbl_makerName.Location = new Point(45, 61);
            lbl_makerName.Name = "lbl_makerName";
            lbl_makerName.Size = new Size(80, 32);
            lbl_makerName.TabIndex = 3;
            lbl_makerName.Text = "maker";
            // 
            // gridView_Maker
            // 
            gridView_Maker.AllowUserToAddRows = false;
            gridView_Maker.AllowUserToDeleteRows = false;
            gridView_Maker.BackgroundColor = Color.White;
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle1.BackColor = SystemColors.Control;
            dataGridViewCellStyle1.Font = new Font("Segoe UI", 9F);
            dataGridViewCellStyle1.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            gridView_Maker.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            gridView_Maker.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gridView_Maker.Columns.AddRange(new DataGridViewColumn[] { dataGridViewTextBoxColumn1, dataGridViewTextBoxColumn2, dataGridViewTextBoxColumn3 });
            gridView_Maker.Location = new Point(45, 133);
            gridView_Maker.Name = "gridView_Maker";
            gridView_Maker.RowHeadersVisible = false;
            gridView_Maker.RowHeadersWidth = 82;
            gridView_Maker.Size = new Size(598, 505);
            gridView_Maker.TabIndex = 2;
            // 
            // dataGridViewTextBoxColumn1
            // 
            dataGridViewTextBoxColumn1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewTextBoxColumn1.HeaderText = "Ask";
            dataGridViewTextBoxColumn1.MinimumWidth = 10;
            dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            // 
            // dataGridViewTextBoxColumn2
            // 
            dataGridViewTextBoxColumn2.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewTextBoxColumn2.HeaderText = "Price";
            dataGridViewTextBoxColumn2.MinimumWidth = 10;
            dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
            // 
            // dataGridViewTextBoxColumn3
            // 
            dataGridViewTextBoxColumn3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewTextBoxColumn3.HeaderText = "Bid";
            dataGridViewTextBoxColumn3.MinimumWidth = 10;
            dataGridViewTextBoxColumn3.Name = "dataGridViewTextBoxColumn3";
            // 
            // gridView_Taker
            // 
            gridView_Taker.AllowUserToAddRows = false;
            gridView_Taker.AllowUserToDeleteRows = false;
            gridView_Taker.BackgroundColor = Color.White;
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle2.BackColor = SystemColors.Control;
            dataGridViewCellStyle2.Font = new Font("Segoe UI", 9F);
            dataGridViewCellStyle2.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle2.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.True;
            gridView_Taker.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            gridView_Taker.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gridView_Taker.Columns.AddRange(new DataGridViewColumn[] { col_Ask, col_price, col_Bid });
            gridView_Taker.Location = new Point(809, 133);
            gridView_Taker.Name = "gridView_Taker";
            gridView_Taker.RowHeadersVisible = false;
            gridView_Taker.RowHeadersWidth = 82;
            gridView_Taker.Size = new Size(598, 505);
            gridView_Taker.TabIndex = 1;
            // 
            // col_Ask
            // 
            col_Ask.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col_Ask.HeaderText = "Ask";
            col_Ask.MinimumWidth = 10;
            col_Ask.Name = "col_Ask";
            // 
            // col_price
            // 
            col_price.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col_price.HeaderText = "Price";
            col_price.MinimumWidth = 10;
            col_price.Name = "col_price";
            // 
            // col_Bid
            // 
            col_Bid.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col_Bid.HeaderText = "Bid";
            col_Bid.MinimumWidth = 10;
            col_Bid.Name = "col_Bid";
            // 
            // tabPage2
            // 
            tabPage2.BackColor = Color.WhiteSmoke;
            tabPage2.Controls.Add(gridView_Ins);
            tabPage2.Controls.Add(lbl_notional);
            tabPage2.Controls.Add(lbl_lastprice);
            tabPage2.Controls.Add(lbl_market);
            tabPage2.Controls.Add(lbl_symbol);
            tabPage2.Controls.Add(label4);
            tabPage2.Controls.Add(label3);
            tabPage2.Controls.Add(label2);
            tabPage2.Controls.Add(label1);
            tabPage2.Controls.Add(comboSymbols);
            tabPage2.Location = new Point(8, 46);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(1539, 896);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "Instrument";
            // 
            // gridView_Ins
            // 
            gridView_Ins.AllowUserToAddRows = false;
            gridView_Ins.AllowUserToDeleteRows = false;
            gridView_Ins.BackgroundColor = Color.White;
            dataGridViewCellStyle3.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle3.BackColor = SystemColors.Control;
            dataGridViewCellStyle3.Font = new Font("Segoe UI", 9F);
            dataGridViewCellStyle3.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle3.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle3.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = DataGridViewTriState.True;
            gridView_Ins.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle3;
            gridView_Ins.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gridView_Ins.Columns.AddRange(new DataGridViewColumn[] { dataGridViewTextBoxColumn4, dataGridViewTextBoxColumn5, dataGridViewTextBoxColumn6 });
            gridView_Ins.Location = new Point(831, 81);
            gridView_Ins.Name = "gridView_Ins";
            gridView_Ins.RowHeadersVisible = false;
            gridView_Ins.RowHeadersWidth = 82;
            gridView_Ins.Size = new Size(598, 505);
            gridView_Ins.TabIndex = 9;
            // 
            // dataGridViewTextBoxColumn4
            // 
            dataGridViewTextBoxColumn4.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewTextBoxColumn4.HeaderText = "Ask";
            dataGridViewTextBoxColumn4.MinimumWidth = 10;
            dataGridViewTextBoxColumn4.Name = "dataGridViewTextBoxColumn4";
            // 
            // dataGridViewTextBoxColumn5
            // 
            dataGridViewTextBoxColumn5.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewTextBoxColumn5.HeaderText = "Price";
            dataGridViewTextBoxColumn5.MinimumWidth = 10;
            dataGridViewTextBoxColumn5.Name = "dataGridViewTextBoxColumn5";
            // 
            // dataGridViewTextBoxColumn6
            // 
            dataGridViewTextBoxColumn6.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewTextBoxColumn6.HeaderText = "Bid";
            dataGridViewTextBoxColumn6.MinimumWidth = 10;
            dataGridViewTextBoxColumn6.Name = "dataGridViewTextBoxColumn6";
            // 
            // lbl_notional
            // 
            lbl_notional.AutoSize = true;
            lbl_notional.Location = new Point(351, 330);
            lbl_notional.Name = "lbl_notional";
            lbl_notional.Size = new Size(71, 32);
            lbl_notional.TabIndex = 8;
            lbl_notional.Text = "value";
            // 
            // lbl_lastprice
            // 
            lbl_lastprice.AutoSize = true;
            lbl_lastprice.Location = new Point(351, 267);
            lbl_lastprice.Name = "lbl_lastprice";
            lbl_lastprice.Size = new Size(71, 32);
            lbl_lastprice.TabIndex = 7;
            lbl_lastprice.Text = "value";
            // 
            // lbl_market
            // 
            lbl_market.AutoSize = true;
            lbl_market.Location = new Point(169, 156);
            lbl_market.Name = "lbl_market";
            lbl_market.Size = new Size(71, 32);
            lbl_market.TabIndex = 6;
            lbl_market.Text = "value";
            // 
            // lbl_symbol
            // 
            lbl_symbol.AutoSize = true;
            lbl_symbol.Location = new Point(169, 105);
            lbl_symbol.Name = "lbl_symbol";
            lbl_symbol.Size = new Size(71, 32);
            lbl_symbol.TabIndex = 5;
            lbl_symbol.Text = "value";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(38, 267);
            label4.Name = "label4";
            label4.Size = new Size(118, 32);
            label4.TabIndex = 4;
            label4.Text = "Last Price:";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(38, 330);
            label3.Name = "label3";
            label3.Size = new Size(199, 32);
            label3.TabIndex = 3;
            label3.Text = "Notional Volume:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(38, 156);
            label2.Name = "label2";
            label2.Size = new Size(94, 32);
            label2.TabIndex = 2;
            label2.Text = "Market:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(38, 105);
            label1.Name = "label1";
            label1.Size = new Size(98, 32);
            label1.TabIndex = 1;
            label1.Text = "Symbol:";
            // 
            // comboSymbols
            // 
            comboSymbols.FormattingEnabled = true;
            comboSymbols.Location = new Point(38, 26);
            comboSymbols.Name = "comboSymbols";
            comboSymbols.Size = new Size(242, 40);
            comboSymbols.TabIndex = 0;
            comboSymbols.SelectedIndexChanged += comboSymbols_SelectedIndexChanged;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(13F, 32F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1571, 962);
            Controls.Add(tabControl);
            Name = "Form1";
            Text = "Form1";
            tabControl.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tabPage3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)gridView_Maker).EndInit();
            ((System.ComponentModel.ISupportInitialize)gridView_Taker).EndInit();
            tabPage2.ResumeLayout(false);
            tabPage2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)gridView_Ins).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Button button1;
        private TabControl tabControl;
        private TabPage tabPage1;
        private TabPage tabPage2;
        private ComboBox comboSymbols;
        private Label label3;
        private Label label2;
        private Label label1;
        private Label lbl_market;
        private Label lbl_symbol;
        private Label label4;
        private Label lbl_quotes;
        private Label lbl_notional;
        private Label lbl_lastprice;
        private TabPage tabPage3;
        private Button button2;
        private RichTextBox textBoxMainLog;
        private GroupBox groupBox1;
        private DataGridView gridView_Taker;
        private DataGridViewTextBoxColumn col_Ask;
        private DataGridViewTextBoxColumn col_price;
        private DataGridViewTextBoxColumn col_Bid;
        private DataGridView gridView_Maker;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn3;
        private DataGridView gridView_Ins;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn4;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn5;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn6;
        private Label lbl_takerName;
        private Label lbl_makerName;
    }
}
