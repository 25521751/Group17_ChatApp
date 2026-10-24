namespace ClientForm
{
    public partial class Login : Form
    {
        public Login()
        {
            InitializeComponent();
            this.BackColor = AppColors.Background;
            this.FormBorderColor = AppColors.Border;
        }

        private void btn_Login_MouseEnter(object sender, EventArgs e)
        {
            btn_Login.BackColor = AppColors.PrimaryHover;
            btn_Login.ForeColor = AppColors.Selected;
        }

        private void btn_Login_MouseLeave(object sender, EventArgs e)
        {
            btn_Login.BackColor = Color.White;
            btn_Login.ForeColor = Color.Black;
        }

        private void btn_Register_MouseEnter(object sender, EventArgs e)
        {
            btn_Register.BackColor = AppColors.PrimaryHover;
            btn_Register.ForeColor = AppColors.Selected;
        }

        private void btn_Register_MouseLeave(object sender, EventArgs e)
        {
            btn_Register.BackColor = Color.White;
            btn_Register.ForeColor = Color.Black;
        }

        private void btn_Register_Click(object sender, EventArgs e)
        {
            var DangKy = new Register();
            DangKy.ShowDialog();
        }

        private void lkb_ForgotPassword_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var Quenmk = new ForgotPassword();
            Quenmk.ShowDialog();
        }

        private bool KiemTraDangNhap(string username, string password)
        {
            if (username == "admin" && password == "admin")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void btn_Login_Click(object sender, EventArgs e)
        {
            string username = txtb_Username.Text.Trim();
            string password = txtbox_Password.Text;

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ tài khoản và mật khẩu!");
                return;
            }

            if (KiemTraDangNhap(username, password))
            {
                MessageBox.Show("Đăng nhập thành công!");

                this.Hide();
                GiaoDien giaoDien = new GiaoDien();
                giaoDien.ShowDialog();

                this.Close();
            }
            else
            {
                MessageBox.Show("Tên đăng nhập hoặc mật khẩu không đúng!");
            }
        }

        private void lb_UserName_Click(object sender, EventArgs e)
        {

        }

        private void txtb_Username_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
