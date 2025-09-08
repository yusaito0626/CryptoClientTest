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
            button1 = new Button();
            labeltest1 = new Label();
            tabControl = new TabControl();
            tabPage1 = new TabPage();
            tabPage2 = new TabPage();
            comboSymbols = new ComboBox();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            lbl_symbol = new Label();
            lbl_market = new Label();
            lbl_lastprice = new Label();
            lbl_notional = new Label();
            lbl_quotes = new Label();
            tabControl.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
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
            // labeltest1
            // 
            labeltest1.Location = new Point(6, 18);
            labeltest1.Name = "labeltest1";
            labeltest1.Size = new Size(702, 498);
            labeltest1.TabIndex = 1;
            labeltest1.Text = "label1";
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabPage1);
            tabControl.Controls.Add(tabPage2);
            tabControl.Location = new Point(12, 12);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(1555, 950);
            tabControl.TabIndex = 4;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(labeltest1);
            tabPage1.Controls.Add(button1);
            tabPage1.Location = new Point(8, 46);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1539, 896);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Main";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(lbl_quotes);
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
            tabPage2.UseVisualStyleBackColor = true;
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
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(38, 105);
            label1.Name = "label1";
            label1.Size = new Size(98, 32);
            label1.TabIndex = 1;
            label1.Text = "Symbol:";
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
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(38, 330);
            label3.Name = "label3";
            label3.Size = new Size(199, 32);
            label3.TabIndex = 3;
            label3.Text = "Notional Volume:";
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
            // lbl_symbol
            // 
            lbl_symbol.AutoSize = true;
            lbl_symbol.Location = new Point(169, 105);
            lbl_symbol.Name = "lbl_symbol";
            lbl_symbol.Size = new Size(71, 32);
            lbl_symbol.TabIndex = 5;
            lbl_symbol.Text = "value";
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
            // lbl_lastprice
            // 
            lbl_lastprice.AutoSize = true;
            lbl_lastprice.Location = new Point(351, 267);
            lbl_lastprice.Name = "lbl_lastprice";
            lbl_lastprice.Size = new Size(71, 32);
            lbl_lastprice.TabIndex = 7;
            lbl_lastprice.Text = "value";
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
            // lbl_quotes
            // 
            lbl_quotes.AutoSize = true;
            lbl_quotes.Location = new Point(763, 92);
            lbl_quotes.Name = "lbl_quotes";
            lbl_quotes.Size = new Size(71, 32);
            lbl_quotes.TabIndex = 9;
            lbl_quotes.Text = "value";
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
            tabPage2.ResumeLayout(false);
            tabPage2.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Button button1;
        private Label labeltest1;
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
    }
}
