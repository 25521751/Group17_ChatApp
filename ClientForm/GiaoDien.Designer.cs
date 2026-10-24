namespace ClientForm
{
    partial class GiaoDien
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
            btn_Chat = new Button();
            btn_Friend = new Button();
            btn_GroupChat = new Button();
            btn_Setting = new Button();
            pnl_Menu = new Panel();
            btn_LogOut = new Button();
            pnl_Chat = new Panel();
            pnl_List = new Panel();
            pnl_Chatting = new Panel();
            pnl_Setting = new Panel();
            pnl_Friend = new Panel();
            pnl_GroupChat = new Panel();
            pnl_Menu.SuspendLayout();
            pnl_Chat.SuspendLayout();
            SuspendLayout();
            // 
            // btn_Chat
            // 
            btn_Chat.FlatAppearance.BorderSize = 0;
            btn_Chat.FlatStyle = FlatStyle.Flat;
            btn_Chat.Location = new Point(0, 0);
            btn_Chat.Margin = new Padding(3, 4, 3, 4);
            btn_Chat.Name = "btn_Chat";
            btn_Chat.Size = new Size(143, 60);
            btn_Chat.TabIndex = 1;
            btn_Chat.Text = "Tin Nhắn";
            btn_Chat.UseVisualStyleBackColor = true;
            btn_Chat.Click += btn_Chat_Click;
            // 
            // btn_Friend
            // 
            btn_Friend.FlatAppearance.BorderSize = 0;
            btn_Friend.FlatStyle = FlatStyle.Flat;
            btn_Friend.Location = new Point(0, 56);
            btn_Friend.Margin = new Padding(3, 4, 3, 4);
            btn_Friend.Name = "btn_Friend";
            btn_Friend.Size = new Size(143, 60);
            btn_Friend.TabIndex = 2;
            btn_Friend.Text = "Bạn Bè";
            btn_Friend.UseVisualStyleBackColor = true;
            btn_Friend.Click += btn_Friend_Click;
            // 
            // btn_GroupChat
            // 
            btn_GroupChat.FlatAppearance.BorderSize = 0;
            btn_GroupChat.FlatStyle = FlatStyle.Flat;
            btn_GroupChat.Location = new Point(0, 109);
            btn_GroupChat.Margin = new Padding(3, 4, 3, 4);
            btn_GroupChat.Name = "btn_GroupChat";
            btn_GroupChat.Size = new Size(143, 60);
            btn_GroupChat.TabIndex = 3;
            btn_GroupChat.Text = "Nhóm Chat";
            btn_GroupChat.UseVisualStyleBackColor = true;
            btn_GroupChat.Click += btn_GroupChat_Click;
            // 
            // btn_Setting
            // 
            btn_Setting.FlatAppearance.BorderSize = 0;
            btn_Setting.FlatStyle = FlatStyle.Flat;
            btn_Setting.Location = new Point(0, 165);
            btn_Setting.Margin = new Padding(3, 4, 3, 4);
            btn_Setting.Name = "btn_Setting";
            btn_Setting.Size = new Size(143, 60);
            btn_Setting.TabIndex = 4;
            btn_Setting.Text = "Cài Đặt";
            btn_Setting.UseVisualStyleBackColor = true;
            btn_Setting.Click += btn_Setting_Click;
            // 
            // pnl_Menu
            // 
            pnl_Menu.BackColor = Color.FromArgb(255, 255, 255);
            pnl_Menu.Controls.Add(btn_LogOut);
            pnl_Menu.Controls.Add(btn_Chat);
            pnl_Menu.Controls.Add(btn_Setting);
            pnl_Menu.Controls.Add(btn_Friend);
            pnl_Menu.Controls.Add(btn_GroupChat);
            pnl_Menu.Dock = DockStyle.Left;
            pnl_Menu.Location = new Point(0, 0);
            pnl_Menu.Margin = new Padding(3, 4, 3, 4);
            pnl_Menu.Name = "pnl_Menu";
            pnl_Menu.Size = new Size(143, 809);
            pnl_Menu.TabIndex = 5;
            // 
            // btn_LogOut
            // 
            btn_LogOut.Dock = DockStyle.Bottom;
            btn_LogOut.FlatAppearance.BorderSize = 0;
            btn_LogOut.FlatStyle = FlatStyle.Flat;
            btn_LogOut.ForeColor = Color.FromArgb(100, 116, 139);
            btn_LogOut.Location = new Point(0, 749);
            btn_LogOut.Margin = new Padding(3, 4, 3, 4);
            btn_LogOut.Name = "btn_LogOut";
            btn_LogOut.Size = new Size(143, 60);
            btn_LogOut.TabIndex = 6;
            btn_LogOut.Text = "Đăng Xuất";
            btn_LogOut.UseVisualStyleBackColor = true;
            btn_LogOut.Click += btn_LogOut_Click;
            btn_LogOut.MouseEnter += btn_LogOut_MouseEnter;
            btn_LogOut.MouseLeave += btn_LogOut_MouseLeave;
            // 
            // pnl_Chat
            // 
            pnl_Chat.Controls.Add(pnl_List);
            pnl_Chat.Controls.Add(pnl_Chatting);
            pnl_Chat.Dock = DockStyle.Fill;
            pnl_Chat.Location = new Point(143, 0);
            pnl_Chat.Margin = new Padding(3, 4, 3, 4);
            pnl_Chat.Name = "pnl_Chat";
            pnl_Chat.Size = new Size(1055, 809);
            pnl_Chat.TabIndex = 6;
            // 
            // pnl_List
            // 
            pnl_List.BackColor = SystemColors.ActiveCaption;
            pnl_List.Dock = DockStyle.Left;
            pnl_List.Location = new Point(0, 0);
            pnl_List.Margin = new Padding(3, 4, 3, 4);
            pnl_List.Name = "pnl_List";
            pnl_List.Size = new Size(210, 809);
            pnl_List.TabIndex = 0;
            pnl_List.Paint += pnl_List_Paint;
            // 
            // pnl_Chatting
            // 
            pnl_Chatting.BackColor = SystemColors.HotTrack;
            pnl_Chatting.Dock = DockStyle.Fill;
            pnl_Chatting.Location = new Point(0, 0);
            pnl_Chatting.Margin = new Padding(3, 4, 3, 4);
            pnl_Chatting.Name = "pnl_Chatting";
            pnl_Chatting.Size = new Size(1055, 809);
            pnl_Chatting.TabIndex = 1;
            // 
            // pnl_Setting
            // 
            pnl_Setting.BackColor = Color.Yellow;
            pnl_Setting.Dock = DockStyle.Fill;
            pnl_Setting.Location = new Point(0, 0);
            pnl_Setting.Margin = new Padding(3, 4, 3, 4);
            pnl_Setting.Name = "pnl_Setting";
            pnl_Setting.Size = new Size(1198, 809);
            pnl_Setting.TabIndex = 7;
            // 
            // pnl_Friend
            // 
            pnl_Friend.BackColor = Color.RosyBrown;
            pnl_Friend.Dock = DockStyle.Fill;
            pnl_Friend.Location = new Point(0, 0);
            pnl_Friend.Margin = new Padding(3, 4, 3, 4);
            pnl_Friend.Name = "pnl_Friend";
            pnl_Friend.Size = new Size(1198, 809);
            pnl_Friend.TabIndex = 8;
            // 
            // pnl_GroupChat
            // 
            pnl_GroupChat.BackColor = SystemColors.ActiveCaption;
            pnl_GroupChat.Dock = DockStyle.Fill;
            pnl_GroupChat.Location = new Point(0, 0);
            pnl_GroupChat.Margin = new Padding(3, 4, 3, 4);
            pnl_GroupChat.Name = "pnl_GroupChat";
            pnl_GroupChat.Size = new Size(1198, 809);
            pnl_GroupChat.TabIndex = 9;
            // 
            // GiaoDien
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1198, 809);
            Controls.Add(pnl_Chat);
            Controls.Add(pnl_Menu);
            Controls.Add(pnl_GroupChat);
            Controls.Add(pnl_Friend);
            Controls.Add(pnl_Setting);
            Margin = new Padding(3, 4, 3, 4);
            Name = "GiaoDien";
            Text = "Form1";
            pnl_Menu.ResumeLayout(false);
            pnl_Chat.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Button btn_Chat;
        private Button btn_Friend;
        private Button btn_GroupChat;
        private Button btn_Setting;
        private Panel pnl_Menu;
        private Button btn_LogOut;
        private Panel pnl_Chat;
        private Panel pnl_Chatting;
        private Panel pnl_List;
        private Panel pnl_Setting;
        private Panel pnl_Friend;
        private Panel pnl_GroupChat;
    }
}