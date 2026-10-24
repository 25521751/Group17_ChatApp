using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ClientForm
{
    public partial class GiaoDien : Form
    {
        public GiaoDien()
        {
            InitializeComponent();
            this.BackColor = AppColors.Background;
            ChonNut(btn_Chat);
            ChuyenPanel(pnl_Chat);
        }

        private void ChonNut(Button nutDuocChon)
        {
            Button[] danhSachNut =
            {
                btn_Chat, btn_Friend, btn_GroupChat, btn_Setting
            };
            foreach (Button nut in danhSachNut)
            {
                nut.BackColor = AppColors.Surface;
                nut.ForeColor = AppColors.TextSecondary;
            }
            nutDuocChon.BackColor = AppColors.Selected;
            nutDuocChon.ForeColor = AppColors.Primary;
        }
        private void ChuyenPanel(Panel PanelDuocChon)
        {
            Panel[] danhSachpanel =
            {
                pnl_Chat, pnl_Friend, pnl_GroupChat, pnl_Setting
            };
            foreach(Panel pnl in danhSachpanel)
            {
                pnl.Visible = false;
            }
            PanelDuocChon.Visible = true;
        }
        private void btn_Chat_Click(object sender, EventArgs e)
        {
            ChonNut(btn_Chat);
            ChuyenPanel(pnl_Chat);
        }

        private void btn_Friend_Click(object sender, EventArgs e)
        {
            ChonNut(btn_Friend);
            ChuyenPanel(pnl_Friend);
        }

        private void btn_GroupChat_Click(object sender, EventArgs e)
        {
            ChonNut(btn_GroupChat);
            ChuyenPanel(pnl_GroupChat);
        }

        private void btn_Setting_Click(object sender, EventArgs e)
        {
            ChonNut(btn_Setting);
            ChuyenPanel(pnl_Setting);
        }

        private void btn_LogOut_MouseEnter(object sender, EventArgs e)
        {
            btn_LogOut.ForeColor = AppColors.Danger;
        }

        private void btn_LogOut_MouseLeave(object sender, EventArgs e)
        {
            btn_LogOut.BackColor = AppColors.Surface;
            btn_LogOut.ForeColor = AppColors.TextSecondary;
        }
    }
}
