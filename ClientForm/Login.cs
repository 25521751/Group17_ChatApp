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
            btn_Login.ForeColor = Color.White;
        }

        private void btn_Login_MouseLeave(object sender, EventArgs e)
        {
            btn_Login.BackColor = Color.White;
            btn_Login.ForeColor = Color.Black;
        }

        private void btn_SignIn_MouseEnter(object sender, EventArgs e)
        {
            btn_SignIn.BackColor = AppColors.PrimaryHover;
            btn_SignIn.ForeColor = Color.White;
        }

        private void btn_SignIn_MouseLeave(object sender, EventArgs e)
        {
            btn_SignIn.BackColor = Color.White;
            btn_SignIn.ForeColor = Color.Black;
        }

        private void btn_SignIn_Click(object sender, EventArgs e)
        {
            var DangKy = new SignIn();
            DangKy.ShowDialog();
        }
    }
}
