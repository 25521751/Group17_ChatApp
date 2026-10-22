namespace ClientForm
{
    partial class Register
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lb_Nofication = new Label();
            lb_Password = new Label();
            lb_UserName = new Label();
            txtbox_Password = new TextBox();
            txtbox_Username = new TextBox();
            Registertext = new Label();
            btn_Register = new Button();
            lb_Email = new Label();
            txtbox_Email = new TextBox();
            lb_text = new Label();
            lkb_Register = new LinkLabel();
            lb_SaiDinhDang = new Label();
            btn_ShowPassword = new Button();
            SuspendLayout();
            // 
            // lb_Nofication
            // 
            lb_Nofication.AutoSize = true;
            lb_Nofication.Font = new Font("Segoe UI", 12F);
            lb_Nofication.Location = new Point(101, 390);
            lb_Nofication.Name = "lb_Nofication";
            lb_Nofication.Size = new Size(470, 42);
            lb_Nofication.TabIndex = 14;
            lb_Nofication.Text = "Lưu ý: Mật khẩu phải có 8 ký tự trở lên trong đó bao gồm 1 chữ số,\r\n1 chữ cái in hoa và 1 ký tự đặc biệt (Ví dụ: K@123)";
            // 
            // lb_Password
            // 
            lb_Password.AutoSize = true;
            lb_Password.Font = new Font("Segoe UI", 17F);
            lb_Password.ForeColor = Color.FromArgb(100, 116, 139);
            lb_Password.Location = new Point(112, 271);
            lb_Password.Name = "lb_Password";
            lb_Password.Size = new Size(121, 31);
            lb_Password.TabIndex = 13;
            lb_Password.Text = "Mật khẩu: ";
            // 
            // lb_UserName
            // 
            lb_UserName.AutoSize = true;
            lb_UserName.Font = new Font("Segoe UI", 17F);
            lb_UserName.ForeColor = Color.FromArgb(100, 116, 139);
            lb_UserName.Location = new Point(111, 140);
            lb_UserName.Name = "lb_UserName";
            lb_UserName.Size = new Size(154, 31);
            lb_UserName.TabIndex = 12;
            lb_UserName.Text = "Tên tài khoản:";
            // 
            // txtbox_Password
            // 
            txtbox_Password.Font = new Font("Segoe UI", 15F);
            txtbox_Password.Location = new Point(271, 268);
            txtbox_Password.Name = "txtbox_Password";
            txtbox_Password.PasswordChar = '*';
            txtbox_Password.Size = new Size(259, 34);
            txtbox_Password.TabIndex = 11;
            // 
            // txtbox_Username
            // 
            txtbox_Username.Font = new Font("Segoe UI", 15F);
            txtbox_Username.Location = new Point(271, 137);
            txtbox_Username.Name = "txtbox_Username";
            txtbox_Username.Size = new Size(259, 34);
            txtbox_Username.TabIndex = 10;
            // 
            // Registertext
            // 
            Registertext.AutoSize = true;
            Registertext.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            Registertext.ForeColor = Color.FromArgb(15, 23, 42);
            Registertext.Location = new Point(244, 83);
            Registertext.Name = "Registertext";
            Registertext.Size = new Size(137, 37);
            Registertext.TabIndex = 9;
            Registertext.Text = "ĐĂNG KÝ";
            // 
            // btn_Register
            // 
            btn_Register.FlatStyle = FlatStyle.Flat;
            btn_Register.Location = new Point(207, 450);
            btn_Register.Name = "btn_Register";
            btn_Register.Size = new Size(204, 47);
            btn_Register.TabIndex = 15;
            btn_Register.Text = "Đăng ký";
            btn_Register.UseVisualStyleBackColor = true;
            btn_Register.Click += btn_Register_Click;
            btn_Register.MouseEnter += btn_Register_MouseEnter;
            btn_Register.MouseLeave += btn_Register_MouseLeave;
            // 
            // lb_Email
            // 
            lb_Email.AutoSize = true;
            lb_Email.Font = new Font("Segoe UI", 17F);
            lb_Email.ForeColor = Color.FromArgb(100, 116, 139);
            lb_Email.Location = new Point(112, 205);
            lb_Email.Name = "lb_Email";
            lb_Email.Size = new Size(75, 31);
            lb_Email.TabIndex = 17;
            lb_Email.Text = "Email:";
            // 
            // txtbox_Email
            // 
            txtbox_Email.Font = new Font("Segoe UI", 15F);
            txtbox_Email.Location = new Point(271, 202);
            txtbox_Email.Name = "txtbox_Email";
            txtbox_Email.Size = new Size(259, 34);
            txtbox_Email.TabIndex = 16;
            // 
            // lb_text
            // 
            lb_text.AutoSize = true;
            lb_text.Font = new Font("Segoe UI", 13F);
            lb_text.Location = new Point(171, 531);
            lb_text.Name = "lb_text";
            lb_text.Size = new Size(144, 25);
            lb_text.TabIndex = 18;
            lb_text.Text = "Đã có tài khoản?";
            // 
            // lkb_Register
            // 
            lkb_Register.AutoSize = true;
            lkb_Register.Font = new Font("Segoe UI", 13F);
            lkb_Register.Location = new Point(315, 531);
            lkb_Register.Name = "lkb_Register";
            lkb_Register.Size = new Size(103, 25);
            lkb_Register.TabIndex = 19;
            lkb_Register.TabStop = true;
            lkb_Register.Text = "Đăng Nhập";
            lkb_Register.LinkClicked += lkb_Register_LinkClicked;
            // 
            // lb_SaiDinhDang
            // 
            lb_SaiDinhDang.AutoSize = true;
            lb_SaiDinhDang.Font = new Font("Segoe UI", 10F);
            lb_SaiDinhDang.ForeColor = Color.FromArgb(220, 38, 38);
            lb_SaiDinhDang.Location = new Point(271, 305);
            lb_SaiDinhDang.Name = "lb_SaiDinhDang";
            lb_SaiDinhDang.Size = new Size(0, 19);
            lb_SaiDinhDang.TabIndex = 20;
            // 
            // btn_ShowPassword
            // 
            btn_ShowPassword.Font = new Font("Segoe UI", 13F);
            btn_ShowPassword.ImageAlign = ContentAlignment.BottomRight;
            btn_ShowPassword.Location = new Point(536, 268);
            btn_ShowPassword.Name = "btn_ShowPassword";
            btn_ShowPassword.Size = new Size(33, 34);
            btn_ShowPassword.TabIndex = 21;
            btn_ShowPassword.Text = "🔒";
            btn_ShowPassword.UseVisualStyleBackColor = true;
            btn_ShowPassword.Click += btn_ShowPassword_Click;
            // 
            // Register
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(636, 649);
            Controls.Add(btn_ShowPassword);
            Controls.Add(lb_SaiDinhDang);
            Controls.Add(lkb_Register);
            Controls.Add(lb_text);
            Controls.Add(lb_Email);
            Controls.Add(txtbox_Email);
            Controls.Add(btn_Register);
            Controls.Add(lb_Nofication);
            Controls.Add(lb_Password);
            Controls.Add(lb_UserName);
            Controls.Add(txtbox_Password);
            Controls.Add(txtbox_Username);
            Controls.Add(Registertext);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "Register";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Register";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lb_Nofication;
        private Label lb_Password;
        private Label lb_UserName;
        private TextBox txtbox_Password;
        private TextBox txtbox_Username;
        private Label Registertext;
        private Button btn_Register;
        private Label lb_Email;
        private TextBox txtbox_Email;
        private Label lb_text;
        private LinkLabel lkb_Register;
        private Label lb_SaiDinhDang;
        private Button btn_ShowPassword;
    }
}