using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ClientForm
{
    public partial class Register : Form
    {
        public Register()
        {
            InitializeComponent();
            this.BackColor = AppColors.Background;
            this.FormBorderColor = AppColors.Border;
        }

        private void btn_Register_MouseEnter(object sender, EventArgs e)
        {
            btn_Register.BackColor = AppColors.Primary;
            btn_Register.ForeColor = Color.White;
        }

        private void btn_Register_MouseLeave(object sender, EventArgs e)
        {
            btn_Register.BackColor = Color.White;
            btn_Register.ForeColor = Color.Black;
        }

        private void lkb_Register_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            this.Close();
        }

        private string KiemTraMatKhau(string matKhau)
        {
            if (matKhau == "")
                return "\nVui lòng nhập mật khẩu";
            bool du8KyTu = matKhau.Length >= 8;
            bool coChuSo = matKhau.Any(char.IsDigit);
            bool coChuHoa = matKhau.Any(char.IsUpper);
            bool coKyTuDacBiet = matKhau.Any(c =>
                !char.IsLetterOrDigit(c));
            if (!(du8KyTu && coChuHoa && coChuHoa && coKyTuDacBiet))
                return "\nMật khẩu không đúng yêu cầu hãy chọn lại";
            else
                return "\n";
        }
        private string KiemtraUser(string User)
        {
            if (User == "")
                return "User không được để trống.";
            return "";
        }
        private string KiemtraEmail(string Email)
        {
            if (Email == "")
                return "\nEmail không được để trống.";
            return "\n";
        }

        private void btn_Register_Click(object sender, EventArgs e)
        {
            string check = KiemtraUser(txtbox_Username.Text) + KiemtraEmail(txtbox_Email.Text) + KiemTraMatKhau(txtbox_Password.Text);
            lb_SaiDinhDang.Text = check;
        }

        private void btn_ShowPassword_Click(object sender, EventArgs e)
        {
            if (txtbox_Password.PasswordChar == '*')
            {
                txtbox_Password.PasswordChar = '\0';
                btn_ShowPassword.Text = "🔓";
            }
            else
            {
                txtbox_Password.PasswordChar = '*';
                btn_ShowPassword.Text = "🔒";
            }
        }

        private void txtbox_Username_TextChanged(object sender, EventArgs e)
        {
            txtbox_Username.MaxLength = 20;
        }

        private void txtbox_Email_TextChanged(object sender, EventArgs e)
        {

        }

        private void txtbox_Password_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
