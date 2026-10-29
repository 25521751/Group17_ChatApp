using ChatappZalu;

namespace ClientForm
{
    public partial class Login : Form
    {
        private bool loginRequestPending = false;

        public Login()
        {
            InitializeComponent();

            this.BackColor = AppColors.Background;
            this.FormBorderColor = AppColors.Border;

            txtbox_Password.PasswordChar = '*';
            btn_ShowPassword.Text = "🔒";

            ChatClientManager.Client.LineReceived += Client_LineReceived;
            this.FormClosed += Login_FormClosed;
        }


        private void Client_LineReceived(string message)
        {
            if (!message.StartsWith("LOGIN_RESULT|"))
                return;

            if (IsDisposed || !IsHandleCreated)
                return;

            BeginInvoke(new Action(() =>
            {
                if (IsDisposed)
                    return;

                loginRequestPending = false;
                btn_Login.Enabled = true;

                string[] parts = message.Split('|');

                if (parts.Length >= 4 &&
                    parts[1] == "OK")
                {
                    MessageBox.Show(
                        "Đăng nhập thành công!",
                        "Zalu",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    this.Hide();

                    GiaoDien giaoDien = new GiaoDien();
                    giaoDien.ShowDialog();

                    this.Close();
                }
                else if (parts.Length >= 3 &&
                         parts[1] == "FAIL")
                {
                    MessageBox.Show(
                        "Tên đăng nhập hoặc mật khẩu không đúng!",
                        "Đăng nhập thất bại",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }));
        }

        private void Login_FormClosed(
            object? sender, FormClosedEventArgs e)
        {
            ChatClientManager.Client.LineReceived -= Client_LineReceived;
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



        private async void btn_Login_Click(object sender, EventArgs e)
        {
            string username = txtb_Username.Text.Trim();
            string password = txtbox_Password.Text;

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(
                    "Vui lòng nhập đầy đủ tài khoản và mật khẩu!");
                return;
            }

            // Ký tự phân cách và xuống dòng không được nằm trong dữ liệu.
            if (username.Contains("|") ||
                password.Contains("|") ||
                username.Contains("\r") ||
                username.Contains("\n") ||
                password.Contains("\r") ||
                password.Contains("\n"))
            {
                MessageBox.Show("Thông tin đăng nhập không hợp lệ!");
                return;
            }

            if (loginRequestPending)
                return;

            loginRequestPending = true;
            btn_Login.Enabled = false;

            try
            {
                var client = ChatClientManager.Client;

                // Chỉ kết nối nếu client chưa kết nối.
                if (!client.IsConnected)
                {
                    await client.ConnectAsync("127.0.0.1", 8080);
                }

                await client.SendLineAsync(
                    $"LOGIN|{username}|{password}");
            }
            catch (Exception ex)
            {
                loginRequestPending = false;
                btn_Login.Enabled = true;

                MessageBox.Show(
                    "Không thể gửi yêu cầu đăng nhập đến server.\n" +
                    "Hãy kiểm tra server và kết nối mạng.\n\n" +
                    ex.Message,
                    "Lỗi kết nối",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


        private void lb_UserName_Click(object sender, EventArgs e)
        {

        }

        private void txtb_Username_TextChanged(object sender, EventArgs e)
        {

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
