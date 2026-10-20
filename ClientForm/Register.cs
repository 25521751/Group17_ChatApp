using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

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

        private bool KiemTraMatKhau(string matKhau)
        {
            bool du8KyTu = matKhau.Length >= 8;
            bool coChuSo = matKhau.Any(char.IsDigit);
            bool coChuHoa = matKhau.Any(char.IsUpper);
            bool coKyTuDacBiet = matKhau.Any(c =>
                !char.IsLetterOrDigit(c));

            return du8KyTu && coChuSo &&
                   coChuHoa && coKyTuDacBiet;
        }

        private void btn_Register_Click(object sender, EventArgs e)
        {
            string check = txtbox_Password.Text;
            if (check == "")
            {
                lb_SaiDinhDang.Text = "Vui lòng nhập mật khẩu";
            }
            else if (!KiemTraMatKhau(check))
            {
                lb_SaiDinhDang.Text = "Mật khẩu không đúng yêu cầu hãy chọn lại";
            }
            else
                lb_SaiDinhDang.Text = "";
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
    }
}
