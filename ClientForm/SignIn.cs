using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ClientForm
{
    public partial class SignIn : Form
    {
        public SignIn()
        {
            InitializeComponent();
            this.BackColor = AppColors.Background;
            this.FormBorderColor = AppColors.Border;
        }

        private void btn_SignIn_MouseEnter(object sender, EventArgs e)
        {
            btn_SignIn.BackColor = AppColors.Primary;
            btn_SignIn.ForeColor = Color.White;
        }

        private void btn_SignIn_MouseLeave(object sender, EventArgs e)
        {
            btn_SignIn.BackColor = Color.White;
            btn_SignIn.ForeColor = Color.Black;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            this.Close();
        }
    }
}
