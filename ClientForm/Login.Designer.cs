
namespace ClientForm
{
    partial class Login
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
            Logintext = new Label();
            txtb_Username = new TextBox();
            txtbox_Password = new TextBox();
            lb_UserName = new Label();
            lb_Password = new Label();
            btn_Login = new Button();
            btn_Register = new Button();
            lkb_ForgotPassword = new LinkLabel();
            btn_ShowPassword = new Button();
            SuspendLayout();
            // 
            // Logintext
            // 
            Logintext.AutoSize = true;
            Logintext.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            Logintext.ForeColor = Color.FromArgb(15, 23, 42);
            Logintext.Location = new Point(261, 136);
            Logintext.Name = "Logintext";
            Logintext.Size = new Size(227, 46);
            Logintext.TabIndex = 1;
            Logintext.Text = "ĐĂNG NHẬP";
            // 
            // txtb_Username
            // 
            txtb_Username.Font = new Font("Segoe UI", 15F);
            txtb_Username.Location = new Point(298, 239);
            txtb_Username.Margin = new Padding(3, 4, 3, 4);
            txtb_Username.Name = "txtb_Username";
            txtb_Username.Size = new Size(313, 41);
            txtb_Username.TabIndex = 2;
            txtb_Username.TextChanged += txtb_Username_TextChanged;
            // 
            // txtbox_Password
            // 
            txtbox_Password.Font = new Font("Segoe UI", 15F);
            txtbox_Password.Location = new Point(298, 318);
            txtbox_Password.Margin = new Padding(3, 4, 3, 4);
            txtbox_Password.Name = "txtbox_Password";
            txtbox_Password.Size = new Size(313, 41);
            txtbox_Password.TabIndex = 3;
            // 
            // lb_UserName
            // 
            lb_UserName.AutoSize = true;
            lb_UserName.Font = new Font("Segoe UI", 17F);
            lb_UserName.ForeColor = Color.FromArgb(100, 116, 139);
            lb_UserName.Location = new Point(91, 240);
            lb_UserName.Name = "lb_UserName";
            lb_UserName.Size = new Size(192, 40);
            lb_UserName.TabIndex = 4;
            lb_UserName.Text = "Tên tài khoản:";
            lb_UserName.Click += lb_UserName_Click;
            // 
            // lb_Password
            // 
            lb_Password.AutoSize = true;
            lb_Password.Font = new Font("Segoe UI", 17F);
            lb_Password.ForeColor = Color.FromArgb(100, 116, 139);
            lb_Password.Location = new Point(91, 318);
            lb_Password.Name = "lb_Password";
            lb_Password.Size = new Size(151, 40);
            lb_Password.TabIndex = 5;
            lb_Password.Text = "Mật khẩu: ";
            // 
            // btn_Login
            // 
            btn_Login.FlatStyle = FlatStyle.Flat;
            btn_Login.Location = new Point(126, 503);
            btn_Login.Margin = new Padding(3, 4, 3, 4);
            btn_Login.Name = "btn_Login";
            btn_Login.Size = new Size(163, 69);
            btn_Login.TabIndex = 6;
            btn_Login.Text = "Đăng nhập";
            btn_Login.UseVisualStyleBackColor = true;
            btn_Login.Click += btn_Login_Click;
            btn_Login.MouseEnter += btn_Login_MouseEnter;
            btn_Login.MouseLeave += btn_Login_MouseLeave;
            // 
            // btn_Register
            // 
            btn_Register.FlatStyle = FlatStyle.Flat;
            btn_Register.Location = new Point(395, 503);
            btn_Register.Margin = new Padding(3, 4, 3, 4);
            btn_Register.Name = "btn_Register";
            btn_Register.Size = new Size(163, 69);
            btn_Register.TabIndex = 7;
            btn_Register.Text = "Đăng ký";
            btn_Register.UseVisualStyleBackColor = true;
            btn_Register.Click += btn_Register_Click;
            btn_Register.MouseEnter += btn_Register_MouseEnter;
            btn_Register.MouseLeave += btn_Register_MouseLeave;
            // 
            // lkb_ForgotPassword
            // 
            lkb_ForgotPassword.AutoSize = true;
            lkb_ForgotPassword.Font = new Font("Segoe UI", 11F);
            lkb_ForgotPassword.Location = new Point(479, 456);
            lkb_ForgotPassword.Name = "lkb_ForgotPassword";
            lkb_ForgotPassword.Size = new Size(150, 25);
            lkb_ForgotPassword.TabIndex = 8;
            lkb_ForgotPassword.TabStop = true;
            lkb_ForgotPassword.Text = "Quên mật khẩu?";
            lkb_ForgotPassword.LinkClicked += lkb_ForgotPassword_LinkClicked;
            // 
            // btn_ShowPassword
            // 
            btn_ShowPassword.Font = new Font("Segoe UI", 13F);
            btn_ShowPassword.ImageAlign = ContentAlignment.BottomRight;
            btn_ShowPassword.Location = new Point(617, 318);
            btn_ShowPassword.Margin = new Padding(3, 4, 3, 4);
            btn_ShowPassword.Name = "btn_ShowPassword";
            btn_ShowPassword.Size = new Size(38, 45);
            btn_ShowPassword.TabIndex = 22;
            btn_ShowPassword.Text = "🔒";
            btn_ShowPassword.UseVisualStyleBackColor = true;
            btn_ShowPassword.Click += btn_ShowPassword_Click;
            // 
            // Login
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(727, 865);
            Controls.Add(btn_ShowPassword);
            Controls.Add(lkb_ForgotPassword);
            Controls.Add(btn_Register);
            Controls.Add(btn_Login);
            Controls.Add(lb_Password);
            Controls.Add(lb_UserName);
            Controls.Add(txtbox_Password);
            Controls.Add(txtb_Username);
            Controls.Add(Logintext);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Margin = new Padding(3, 4, 3, 4);
            MaximizeBox = false;
            Name = "Login";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Đăng nhập";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label Logintext;
        private TextBox txtb_Username;
        private TextBox txtbox_Password;
        private Label lb_UserName;
        private Label lb_Password;
        private Button btn_Login;
        private Button btn_Register;
        private LinkLabel lkb_ForgotPassword;
        private Button btn_ShowPassword;
    }
}
