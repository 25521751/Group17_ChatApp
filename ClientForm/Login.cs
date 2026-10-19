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

        private void btn_Register_MouseEnter(object sender, EventArgs e)
        {
            btn_Register.BackColor = AppColors.PrimaryHover;
            btn_Register.ForeColor = Color.White;
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
    }
}
