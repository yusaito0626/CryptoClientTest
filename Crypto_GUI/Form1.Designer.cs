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
            DataGridViewCellStyle dataGridViewCellStyle4 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle5 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle6 = new DataGridViewCellStyle();
            button1 = new Button();
            tabControl = new TabControl();
            tabPage1 = new TabPage();
            textBoxMainLog = new RichTextBox();
            button2 = new Button();
            tabPage3 = new TabPage();
            lbl_skewpoint = new Label();
            label24 = new Label();
            label21 = new Label();
            lbl_bidprice = new Label();
            lbl_askprice = new Label();
            label20 = new Label();
            lbl_quoteCcy_taker = new Label();
            lbl_baseCcy_taker = new Label();
            label22 = new Label();
            label23 = new Label();
            lbl_quoteCcy_maker = new Label();
            lbl_baseCcy_maker = new Label();
            label19 = new Label();
            label18 = new Label();
            groupBox1 = new GroupBox();
            lbl_ordUpdateTh = new Label();
            lbl_fillInterval = new Label();
            lbl_oneside = new Label();
            lbl_skew = new Label();
            lbl_maxpos = new Label();
            lbl_tobsize = new Label();
            lbl_markup = new Label();
            label17 = new Label();
            label16 = new Label();
            label15 = new Label();
            label14 = new Label();
            label13 = new Label();
            label11 = new Label();
            label8 = new Label();
            lbl_takerfee_taker = new Label();
            label10 = new Label();
            lbl_makerfee_taker = new Label();
            label12 = new Label();
            lbl_takerfee_maker = new Label();
            label9 = new Label();
            lbl_makerfee_maker = new Label();
            label7 = new Label();
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
            lbl_quoteBalance = new Label();
            lbl_baseBalance = new Label();
            label6 = new Label();
            label5 = new Label();
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
            label25 = new Label();
            lbl_adjustedbid = new Label();
            lbl_adjustedask = new Label();
            label28 = new Label();
            tabControl.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage3.SuspendLayout();
            groupBox1.SuspendLayout();
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
            tabControl.Size = new Size(1555, 1286);
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
            tabPage1.Size = new Size(1539, 1232);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Main";
            // 
            // textBoxMainLog
            // 
            textBoxMainLog.Font = new Font("Calibri", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            textBoxMainLog.Location = new Point(9, 614);
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
            tabPage3.Controls.Add(label25);
            tabPage3.Controls.Add(lbl_adjustedbid);
            tabPage3.Controls.Add(lbl_adjustedask);
            tabPage3.Controls.Add(label28);
            tabPage3.Controls.Add(lbl_skewpoint);
            tabPage3.Controls.Add(label24);
            tabPage3.Controls.Add(label21);
            tabPage3.Controls.Add(lbl_bidprice);
            tabPage3.Controls.Add(lbl_askprice);
            tabPage3.Controls.Add(label20);
            tabPage3.Controls.Add(lbl_quoteCcy_taker);
            tabPage3.Controls.Add(lbl_baseCcy_taker);
            tabPage3.Controls.Add(label22);
            tabPage3.Controls.Add(label23);
            tabPage3.Controls.Add(lbl_quoteCcy_maker);
            tabPage3.Controls.Add(lbl_baseCcy_maker);
            tabPage3.Controls.Add(label19);
            tabPage3.Controls.Add(label18);
            tabPage3.Controls.Add(groupBox1);
            tabPage3.Controls.Add(lbl_takerfee_taker);
            tabPage3.Controls.Add(label10);
            tabPage3.Controls.Add(lbl_makerfee_taker);
            tabPage3.Controls.Add(label12);
            tabPage3.Controls.Add(lbl_takerfee_maker);
            tabPage3.Controls.Add(label9);
            tabPage3.Controls.Add(lbl_makerfee_maker);
            tabPage3.Controls.Add(label7);
            tabPage3.Controls.Add(lbl_takerName);
            tabPage3.Controls.Add(lbl_makerName);
            tabPage3.Controls.Add(gridView_Maker);
            tabPage3.Controls.Add(gridView_Taker);
            tabPage3.Location = new Point(8, 46);
            tabPage3.Name = "tabPage3";
            tabPage3.Padding = new Padding(3);
            tabPage3.Size = new Size(1539, 1232);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "Strategy";
            // 
            // lbl_skewpoint
            // 
            lbl_skewpoint.AutoSize = true;
            lbl_skewpoint.Location = new Point(668, 743);
            lbl_skewpoint.Name = "lbl_skewpoint";
            lbl_skewpoint.Size = new Size(71, 32);
            lbl_skewpoint.TabIndex = 27;
            lbl_skewpoint.Text = "value";
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(574, 743);
            label24.Name = "label24";
            label24.Size = new Size(71, 32);
            label24.TabIndex = 26;
            label24.Text = "skew:";
            // 
            // label21
            // 
            label21.AutoSize = true;
            label21.Location = new Point(319, 743);
            label21.Name = "label21";
            label21.Size = new Size(53, 32);
            label21.TabIndex = 25;
            label21.Text = "bid:";
            // 
            // lbl_bidprice
            // 
            lbl_bidprice.AutoSize = true;
            lbl_bidprice.Location = new Point(451, 743);
            lbl_bidprice.Name = "lbl_bidprice";
            lbl_bidprice.Size = new Size(71, 32);
            lbl_bidprice.TabIndex = 24;
            lbl_bidprice.Text = "value";
            // 
            // lbl_askprice
            // 
            lbl_askprice.AutoSize = true;
            lbl_askprice.Location = new Point(177, 743);
            lbl_askprice.Name = "lbl_askprice";
            lbl_askprice.Size = new Size(71, 32);
            lbl_askprice.TabIndex = 23;
            lbl_askprice.Text = "value";
            // 
            // label20
            // 
            label20.AutoSize = true;
            label20.Location = new Point(46, 743);
            label20.Name = "label20";
            label20.Size = new Size(53, 32);
            label20.TabIndex = 22;
            label20.Text = "ask:";
            // 
            // lbl_quoteCcy_taker
            // 
            lbl_quoteCcy_taker.AutoSize = true;
            lbl_quoteCcy_taker.Location = new Point(1224, 799);
            lbl_quoteCcy_taker.Name = "lbl_quoteCcy_taker";
            lbl_quoteCcy_taker.Size = new Size(71, 32);
            lbl_quoteCcy_taker.TabIndex = 21;
            lbl_quoteCcy_taker.Text = "value";
            // 
            // lbl_baseCcy_taker
            // 
            lbl_baseCcy_taker.AutoSize = true;
            lbl_baseCcy_taker.Location = new Point(950, 799);
            lbl_baseCcy_taker.Name = "lbl_baseCcy_taker";
            lbl_baseCcy_taker.Size = new Size(71, 32);
            lbl_baseCcy_taker.TabIndex = 20;
            lbl_baseCcy_taker.Text = "value";
            // 
            // label22
            // 
            label22.AutoSize = true;
            label22.Location = new Point(1092, 799);
            label22.Name = "label22";
            label22.Size = new Size(120, 32);
            label22.TabIndex = 19;
            label22.Text = "quoteCcy:";
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(818, 799);
            label23.Name = "label23";
            label23.Size = new Size(106, 32);
            label23.TabIndex = 18;
            label23.Text = "baseCcy:";
            // 
            // lbl_quoteCcy_maker
            // 
            lbl_quoteCcy_maker.AutoSize = true;
            lbl_quoteCcy_maker.Location = new Point(451, 799);
            lbl_quoteCcy_maker.Name = "lbl_quoteCcy_maker";
            lbl_quoteCcy_maker.Size = new Size(71, 32);
            lbl_quoteCcy_maker.TabIndex = 17;
            lbl_quoteCcy_maker.Text = "value";
            // 
            // lbl_baseCcy_maker
            // 
            lbl_baseCcy_maker.AutoSize = true;
            lbl_baseCcy_maker.Location = new Point(177, 799);
            lbl_baseCcy_maker.Name = "lbl_baseCcy_maker";
            lbl_baseCcy_maker.Size = new Size(71, 32);
            lbl_baseCcy_maker.TabIndex = 16;
            lbl_baseCcy_maker.Text = "value";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(319, 799);
            label19.Name = "label19";
            label19.Size = new Size(120, 32);
            label19.TabIndex = 15;
            label19.Text = "quoteCcy:";
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Location = new Point(45, 799);
            label18.Name = "label18";
            label18.Size = new Size(106, 32);
            label18.TabIndex = 14;
            label18.Text = "baseCcy:";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(lbl_ordUpdateTh);
            groupBox1.Controls.Add(lbl_fillInterval);
            groupBox1.Controls.Add(lbl_oneside);
            groupBox1.Controls.Add(lbl_skew);
            groupBox1.Controls.Add(lbl_maxpos);
            groupBox1.Controls.Add(lbl_tobsize);
            groupBox1.Controls.Add(lbl_markup);
            groupBox1.Controls.Add(label17);
            groupBox1.Controls.Add(label16);
            groupBox1.Controls.Add(label15);
            groupBox1.Controls.Add(label14);
            groupBox1.Controls.Add(label13);
            groupBox1.Controls.Add(label11);
            groupBox1.Controls.Add(label8);
            groupBox1.Location = new Point(6, 866);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(1518, 360);
            groupBox1.TabIndex = 13;
            groupBox1.TabStop = false;
            groupBox1.Text = "Setting";
            // 
            // lbl_ordUpdateTh
            // 
            lbl_ordUpdateTh.AutoSize = true;
            lbl_ordUpdateTh.Location = new Point(957, 124);
            lbl_ordUpdateTh.Name = "lbl_ordUpdateTh";
            lbl_ordUpdateTh.Size = new Size(71, 32);
            lbl_ordUpdateTh.TabIndex = 20;
            lbl_ordUpdateTh.Text = "value";
            // 
            // lbl_fillInterval
            // 
            lbl_fillInterval.AutoSize = true;
            lbl_fillInterval.Location = new Point(957, 69);
            lbl_fillInterval.Name = "lbl_fillInterval";
            lbl_fillInterval.Size = new Size(71, 32);
            lbl_fillInterval.TabIndex = 19;
            lbl_fillInterval.Text = "value";
            // 
            // lbl_oneside
            // 
            lbl_oneside.AutoSize = true;
            lbl_oneside.Location = new Point(232, 286);
            lbl_oneside.Name = "lbl_oneside";
            lbl_oneside.Size = new Size(71, 32);
            lbl_oneside.TabIndex = 18;
            lbl_oneside.Text = "value";
            // 
            // lbl_skew
            // 
            lbl_skew.AutoSize = true;
            lbl_skew.Location = new Point(232, 233);
            lbl_skew.Name = "lbl_skew";
            lbl_skew.Size = new Size(71, 32);
            lbl_skew.TabIndex = 17;
            lbl_skew.Text = "value";
            // 
            // lbl_maxpos
            // 
            lbl_maxpos.AutoSize = true;
            lbl_maxpos.Location = new Point(232, 179);
            lbl_maxpos.Name = "lbl_maxpos";
            lbl_maxpos.Size = new Size(71, 32);
            lbl_maxpos.TabIndex = 16;
            lbl_maxpos.Text = "value";
            // 
            // lbl_tobsize
            // 
            lbl_tobsize.AutoSize = true;
            lbl_tobsize.Location = new Point(232, 124);
            lbl_tobsize.Name = "lbl_tobsize";
            lbl_tobsize.Size = new Size(71, 32);
            lbl_tobsize.TabIndex = 15;
            lbl_tobsize.Text = "value";
            // 
            // lbl_markup
            // 
            lbl_markup.AutoSize = true;
            lbl_markup.Location = new Point(232, 69);
            lbl_markup.Name = "lbl_markup";
            lbl_markup.Size = new Size(71, 32);
            lbl_markup.TabIndex = 14;
            lbl_markup.Text = "value";
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(738, 124);
            label17.Name = "label17";
            label17.Size = new Size(209, 32);
            label17.TabIndex = 12;
            label17.Text = "Update Threshold:";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(738, 69);
            label16.Name = "label16";
            label16.Size = new Size(194, 32);
            label16.TabIndex = 11;
            label16.Text = "Interval After Fill:";
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(30, 286);
            label15.Name = "label15";
            label15.Size = new Size(191, 32);
            label15.TabIndex = 10;
            label15.Text = "One Side Quote:";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(30, 233);
            label14.Name = "label14";
            label14.Size = new Size(136, 32);
            label14.TabIndex = 9;
            label14.Text = "Skew Level:";
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(30, 179);
            label13.Name = "label13";
            label13.Size = new Size(155, 32);
            label13.TabIndex = 8;
            label13.Text = "Max Position:";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(30, 124);
            label11.Name = "label11";
            label11.Size = new Size(105, 32);
            label11.TabIndex = 7;
            label11.Text = "ToB size:";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(30, 69);
            label8.Name = "label8";
            label8.Size = new Size(101, 32);
            label8.TabIndex = 6;
            label8.Text = "Markup:";
            // 
            // lbl_takerfee_taker
            // 
            lbl_takerfee_taker.AutoSize = true;
            lbl_takerfee_taker.Location = new Point(1224, 684);
            lbl_takerfee_taker.Name = "lbl_takerfee_taker";
            lbl_takerfee_taker.Size = new Size(71, 32);
            lbl_takerfee_taker.TabIndex = 12;
            lbl_takerfee_taker.Text = "value";
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(1092, 684);
            label10.Name = "label10";
            label10.Size = new Size(113, 32);
            label10.TabIndex = 11;
            label10.Text = "taker fee:";
            // 
            // lbl_makerfee_taker
            // 
            lbl_makerfee_taker.AutoSize = true;
            lbl_makerfee_taker.Location = new Point(950, 684);
            lbl_makerfee_taker.Name = "lbl_makerfee_taker";
            lbl_makerfee_taker.Size = new Size(71, 32);
            lbl_makerfee_taker.TabIndex = 10;
            lbl_makerfee_taker.Text = "value";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(818, 684);
            label12.Name = "label12";
            label12.Size = new Size(126, 32);
            label12.TabIndex = 9;
            label12.Text = "maker fee:";
            // 
            // lbl_takerfee_maker
            // 
            lbl_takerfee_maker.AutoSize = true;
            lbl_takerfee_maker.Location = new Point(451, 684);
            lbl_takerfee_maker.Name = "lbl_takerfee_maker";
            lbl_takerfee_maker.Size = new Size(71, 32);
            lbl_takerfee_maker.TabIndex = 8;
            lbl_takerfee_maker.Text = "value";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(319, 684);
            label9.Name = "label9";
            label9.Size = new Size(113, 32);
            label9.TabIndex = 7;
            label9.Text = "taker fee:";
            // 
            // lbl_makerfee_maker
            // 
            lbl_makerfee_maker.AutoSize = true;
            lbl_makerfee_maker.Location = new Point(177, 684);
            lbl_makerfee_maker.Name = "lbl_makerfee_maker";
            lbl_makerfee_maker.Size = new Size(71, 32);
            lbl_makerfee_maker.TabIndex = 6;
            lbl_makerfee_maker.Text = "value";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(45, 684);
            label7.Name = "label7";
            label7.Size = new Size(126, 32);
            label7.TabIndex = 5;
            label7.Text = "maker fee:";
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
            dataGridViewCellStyle4.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle4.BackColor = SystemColors.Control;
            dataGridViewCellStyle4.Font = new Font("Segoe UI", 9F);
            dataGridViewCellStyle4.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle4.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle4.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle4.WrapMode = DataGridViewTriState.True;
            gridView_Maker.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle4;
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
            dataGridViewCellStyle5.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle5.BackColor = SystemColors.Control;
            dataGridViewCellStyle5.Font = new Font("Segoe UI", 9F);
            dataGridViewCellStyle5.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle5.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle5.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle5.WrapMode = DataGridViewTriState.True;
            gridView_Taker.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle5;
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
            tabPage2.Controls.Add(lbl_quoteBalance);
            tabPage2.Controls.Add(lbl_baseBalance);
            tabPage2.Controls.Add(label6);
            tabPage2.Controls.Add(label5);
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
            tabPage2.Size = new Size(1539, 1232);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "Instrument";
            // 
            // lbl_quoteBalance
            // 
            lbl_quoteBalance.AutoSize = true;
            lbl_quoteBalance.Location = new Point(868, 675);
            lbl_quoteBalance.Name = "lbl_quoteBalance";
            lbl_quoteBalance.Size = new Size(71, 32);
            lbl_quoteBalance.TabIndex = 13;
            lbl_quoteBalance.Text = "value";
            // 
            // lbl_baseBalance
            // 
            lbl_baseBalance.AutoSize = true;
            lbl_baseBalance.Location = new Point(246, 675);
            lbl_baseBalance.Name = "lbl_baseBalance";
            lbl_baseBalance.Size = new Size(71, 32);
            lbl_baseBalance.TabIndex = 12;
            lbl_baseBalance.Text = "value";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(642, 675);
            label6.Name = "label6";
            label6.Size = new Size(220, 32);
            label6.TabIndex = 11;
            label6.Text = "Quote Ccy Balance:";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(38, 675);
            label5.Name = "label5";
            label5.Size = new Size(202, 32);
            label5.TabIndex = 10;
            label5.Text = "Base Ccy Balance:";
            // 
            // gridView_Ins
            // 
            gridView_Ins.AllowUserToAddRows = false;
            gridView_Ins.AllowUserToDeleteRows = false;
            gridView_Ins.BackgroundColor = Color.White;
            dataGridViewCellStyle6.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle6.BackColor = SystemColors.Control;
            dataGridViewCellStyle6.Font = new Font("Segoe UI", 9F);
            dataGridViewCellStyle6.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle6.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle6.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle6.WrapMode = DataGridViewTriState.True;
            gridView_Ins.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle6;
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
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new Point(1091, 743);
            label25.Name = "label25";
            label25.Size = new Size(53, 32);
            label25.TabIndex = 31;
            label25.Text = "bid:";
            // 
            // lbl_adjustedbid
            // 
            lbl_adjustedbid.AutoSize = true;
            lbl_adjustedbid.Location = new Point(1223, 743);
            lbl_adjustedbid.Name = "lbl_adjustedbid";
            lbl_adjustedbid.Size = new Size(71, 32);
            lbl_adjustedbid.TabIndex = 30;
            lbl_adjustedbid.Text = "value";
            // 
            // lbl_adjustedask
            // 
            lbl_adjustedask.AutoSize = true;
            lbl_adjustedask.Location = new Point(949, 743);
            lbl_adjustedask.Name = "lbl_adjustedask";
            lbl_adjustedask.Size = new Size(71, 32);
            lbl_adjustedask.TabIndex = 29;
            lbl_adjustedask.Text = "value";
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Location = new Point(818, 743);
            label28.Name = "label28";
            label28.Size = new Size(53, 32);
            label28.TabIndex = 28;
            label28.Text = "ask:";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(13F, 32F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1574, 1310);
            Controls.Add(tabControl);
            Name = "Form1";
            Text = "Form1";
            tabControl.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tabPage3.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
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
        private Label label5;
        private Label lbl_quoteBalance;
        private Label lbl_baseBalance;
        private Label label6;
        private Label lbl_makerfee_maker;
        private Label label7;
        private Label lbl_takerfee_maker;
        private Label label9;
        private Label lbl_takerfee_taker;
        private Label label10;
        private Label lbl_makerfee_taker;
        private Label label12;
        private Label label11;
        private Label label8;
        private Label label13;
        private Label label15;
        private Label label14;
        private Label label17;
        private Label label16;
        private Label lbl_maxpos;
        private Label lbl_tobsize;
        private Label lbl_markup;
        private Label lbl_ordUpdateTh;
        private Label lbl_fillInterval;
        private Label lbl_oneside;
        private Label lbl_skew;
        private Label lbl_quoteCcy_maker;
        private Label lbl_baseCcy_maker;
        private Label label19;
        private Label label18;
        private Label lbl_quoteCcy_taker;
        private Label lbl_baseCcy_taker;
        private Label label22;
        private Label label23;
        private Label label21;
        private Label lbl_bidprice;
        private Label lbl_askprice;
        private Label label20;
        private Label label24;
        private Label lbl_skewpoint;
        private Label label25;
        private Label lbl_adjustedbid;
        private Label lbl_adjustedask;
        private Label label28;
    }
}
