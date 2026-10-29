using ChatappZalu;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

namespace ClientForm
{
    public partial class GiaoDien : Form
    {
        private bool logoutRequestPending = false;
        private bool logoutCompleted = false;
        public GiaoDien()
        {
            InitializeComponent();
            ChatClientManager.Client.LineReceived += Client_LineReceived;
            this.FormClosed += GiaoDien_FormClosed;

            this.BackColor = AppColors.Background;
            ChonNut(btn_Chat);
            ChuyenPanel(pnl_Chat);
        }


        private void Client_LineReceived(string message)
        {
            if (!message.StartsWith("LOGOUT_RESULT|"))
                return;

            if (IsDisposed || !IsHandleCreated)
                return;

            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || !logoutRequestPending)
                    return;

                string[] parts = message.Split('|');

                logoutRequestPending = false;

                if (parts.Length >= 3 && parts[1] == "OK")
                {
                    logoutCompleted = true;

                    MessageBox.Show(
                        "Đăng xuất thành công!",
                        "Zalu",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    // Server đã xác nhận đăng xuất.
                    // Đóng kết nối vì server đã xử lý LOGOUT.
                    ChatClientManager.Client.Disconnect();

                    this.Hide();

                    Login login = new Login();
                    login.ShowDialog();

                    this.Close();
                }
                else
                {
                    MessageBox.Show(
                        "Server không xác nhận được yêu cầu đăng xuất.",
                        "Zalu",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }));
        }

        private void GiaoDien_FormClosed(
            object? sender, FormClosedEventArgs e)
        {
            ChatClientManager.Client.LineReceived -= Client_LineReceived;
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
            foreach (Panel pnl in danhSachpanel)
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

        private void pnl_Chatting_Paint(object sender, PaintEventArgs e)
        {

        }

        private void pnl_List_Paint(object sender, PaintEventArgs e)
        {

        }



        private async void btn_LogOut_Click(object sender, EventArgs e)
        {
            var client = ChatClientManager.Client;

            if (logoutRequestPending)
                return;

            if (!client.IsConnected)
            {
                MessageBox.Show(
                    "Không có kết nối đến server. Không thể xác nhận đăng xuất.",
                    "Zalu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            logoutRequestPending = true;
            btn_LogOut.Enabled = false;

            try
            {
                await client.SendLineAsync("LOGOUT");
            }
            catch (Exception ex)
            {
                logoutRequestPending = false;
                btn_LogOut.Enabled = true;

                MessageBox.Show(
                    "Gửi yêu cầu đăng xuất thất bại.\n" + ex.Message,
                    "Lỗi kết nối",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


    }
}
