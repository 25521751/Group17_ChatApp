namespace ClientForm
{
    partial class SignIn
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
            SignIntext = new Label();
            btn_SignIn = new Button();
            lb_Email = new Label();
            txtbox_Email = new TextBox();
            label1 = new Label();
            linkLabel1 = new LinkLabel();
            SuspendLayout();
            // 
            // lb_Nofication
            // 
            lb_Nofication.AutoSize = true;
            lb_Nofication.Font = new Font("Segoe UI", 12F);
            lb_Nofication.Location = new Point(99, 389);
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
            lb_Password.Location = new Point(130, 295);
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
            lb_UserName.Location = new Point(130, 161);
            lb_UserName.Name = "lb_UserName";
            lb_UserName.Size = new Size(154, 31);
            lb_UserName.TabIndex = 12;
            lb_UserName.Text = "Tên tài khoản:";
            // 
            // txtbox_Password
            // 
            txtbox_Password.Font = new Font("Segoe UI", 15F);
            txtbox_Password.Location = new Point(301, 292);
            txtbox_Password.Name = "txtbox_Password";
            txtbox_Password.Size = new Size(229, 34);
            txtbox_Password.TabIndex = 11;
            // 
            // txtbox_Username
            // 
            txtbox_Username.Font = new Font("Segoe UI", 15F);
            txtbox_Username.Location = new Point(301, 161);
            txtbox_Username.Name = "txtbox_Username";
            txtbox_Username.Size = new Size(229, 34);
            txtbox_Username.TabIndex = 10;
            // 
            // SignIntext
            // 
            SignIntext.AutoSize = true;
            SignIntext.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            SignIntext.ForeColor = Color.FromArgb(15, 23, 42);
            SignIntext.Location = new Point(240, 71);
            SignIntext.Name = "SignIntext";
            SignIntext.Size = new Size(137, 37);
            SignIntext.TabIndex = 9;
            SignIntext.Text = "ĐĂNG KÝ";
            // 
            // btn_SignIn
            // 
            btn_SignIn.Location = new Point(207, 450);
            btn_SignIn.Name = "btn_SignIn";
            btn_SignIn.Size = new Size(204, 47);
            btn_SignIn.TabIndex = 15;
            btn_SignIn.Text = "Đăng ký";
            btn_SignIn.UseVisualStyleBackColor = true;
            btn_SignIn.MouseEnter += btn_SignIn_MouseEnter;
            btn_SignIn.MouseLeave += btn_SignIn_MouseLeave;
            // 
            // lb_Email
            // 
            lb_Email.AutoSize = true;
            lb_Email.Font = new Font("Segoe UI", 17F);
            lb_Email.ForeColor = Color.FromArgb(100, 116, 139);
            lb_Email.Location = new Point(130, 229);
            lb_Email.Name = "lb_Email";
            lb_Email.Size = new Size(75, 31);
            lb_Email.TabIndex = 17;
            lb_Email.Text = "Email:";
            // 
            // txtbox_Email
            // 
            txtbox_Email.Font = new Font("Segoe UI", 15F);
            txtbox_Email.Location = new Point(301, 226);
            txtbox_Email.Name = "txtbox_Email";
            txtbox_Email.Size = new Size(229, 34);
            txtbox_Email.TabIndex = 16;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 13F);
            label1.Location = new Point(171, 531);
            label1.Name = "label1";
            label1.Size = new Size(144, 25);
            label1.TabIndex = 18;
            label1.Text = "Đã có tài khoản?";
            // 
            // linkLabel1
            // 
            linkLabel1.AutoSize = true;
            linkLabel1.Font = new Font("Segoe UI", 13F);
            linkLabel1.Location = new Point(315, 531);
            linkLabel1.Name = "linkLabel1";
            linkLabel1.Size = new Size(103, 25);
            linkLabel1.TabIndex = 19;
            linkLabel1.TabStop = true;
            linkLabel1.Text = "Đăng Nhập";
            linkLabel1.LinkClicked += linkLabel1_LinkClicked;
            // 
            // SignIn
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(636, 649);
            Controls.Add(linkLabel1);
            Controls.Add(label1);
            Controls.Add(lb_Email);
            Controls.Add(txtbox_Email);
            Controls.Add(btn_SignIn);
            Controls.Add(lb_Nofication);
            Controls.Add(lb_Password);
            Controls.Add(lb_UserName);
            Controls.Add(txtbox_Password);
            Controls.Add(txtbox_Username);
            Controls.Add(SignIntext);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Name = "SignIn";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "SignIn";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lb_Nofication;
        private Label lb_Password;
        private Label lb_UserName;
        private TextBox txtbox_Password;
        private TextBox txtbox_Username;
        private Label SignIntext;
        private Button btn_SignIn;
        private Label lb_Email;
        private TextBox txtbox_Email;
        private Label label1;
        private LinkLabel linkLabel1;
    }
}