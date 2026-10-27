namespace Chat_room_test_TCP
{
    partial class ServerForm
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
            this.Start_Server_Button = new System.Windows.Forms.Button();
            this.Close_Server_Button = new System.Windows.Forms.Button();
            this.Exit_button = new System.Windows.Forms.Button();
            this.Server_ChatView = new System.Windows.Forms.RichTextBox();
            this.SuspendLayout();
            // 
            // Start_Server_Button
            // 
            this.Start_Server_Button.Location = new System.Drawing.Point(28, 12);
            this.Start_Server_Button.Name = "Start_Server_Button";
            this.Start_Server_Button.Size = new System.Drawing.Size(75, 66);
            this.Start_Server_Button.TabIndex = 7;
            this.Start_Server_Button.Text = "Start Server";
            this.Start_Server_Button.UseVisualStyleBackColor = true;
            this.Start_Server_Button.Click += new System.EventHandler(this.Start_Server_Button_Click);
            // 
            // Close_Server_Button
            // 
            this.Close_Server_Button.Location = new System.Drawing.Point(146, 12);
            this.Close_Server_Button.Name = "Close_Server_Button";
            this.Close_Server_Button.Size = new System.Drawing.Size(75, 66);
            this.Close_Server_Button.TabIndex = 8;
            this.Close_Server_Button.Text = "Close Server";
            this.Close_Server_Button.UseVisualStyleBackColor = true;
            this.Close_Server_Button.Click += new System.EventHandler(this.Close_Server_Button_Click);
            // 
            // Exit_button
            // 
            this.Exit_button.Location = new System.Drawing.Point(272, 12);
            this.Exit_button.Name = "Exit_button";
            this.Exit_button.Size = new System.Drawing.Size(75, 66);
            this.Exit_button.TabIndex = 9;
            this.Exit_button.Text = "Exit";
            this.Exit_button.UseVisualStyleBackColor = true;
            this.Exit_button.Click += new System.EventHandler(this.Exit_button_Click);
            // 
            // Server_ChatView
            // 
            this.Server_ChatView.Location = new System.Drawing.Point(28, 104);
            this.Server_ChatView.Name = "Server_ChatView";
            this.Server_ChatView.ReadOnly = true;
            this.Server_ChatView.Size = new System.Drawing.Size(749, 324);
            this.Server_ChatView.TabIndex = 10;
            this.Server_ChatView.Text = "";
            // 
            // ServerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.Server_ChatView);
            this.Controls.Add(this.Exit_button);
            this.Controls.Add(this.Close_Server_Button);
            this.Controls.Add(this.Start_Server_Button);
            this.Name = "ServerForm";
            this.Text = "Form1";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Start_Server_Button;
        private System.Windows.Forms.Button Close_Server_Button;
        private System.Windows.Forms.Button Exit_button;
        private System.Windows.Forms.RichTextBox Server_ChatView;
    }
}

