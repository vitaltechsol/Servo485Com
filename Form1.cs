using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyModbus;

namespace Servo485Com
{
    public partial class Form1 : Form
    {


        ModbusClient mbc = new ModbusClient("COM3")
        {

            Baudrate = 115200,
            UnitIdentifier = 4,
            StopBits = System.IO.Ports.StopBits.Two,
            Parity = System.IO.Ports.Parity.None

        };


        public Form1()
        {
            //   mbc.IPAddress = "192.168.1.173"; // "192.168.1.175";
            //  mbc.Port = 502;
            mbc.SerialPort = "COM3";
            InitializeComponent();
            mbc.UnitIdentifier = Convert.ToByte(this.txbDriverID.Text);
        }

        // Change Custom
        private void button1_Click(object sender, EventArgs e)
        {

            label1.Text = "";

            try
            {
                mbc.Connect();
                mbc.WriteSingleRegister(Int32.Parse(textBox3.Text), Int32.Parse(textBox1.Text));
                label1.Text = "done";
                int[] results = mbc.ReadHoldingRegisters(Int32.Parse(textBox3.Text), 1);
                textBox2.Text += "set: " + textBox3.Text + ": " + results[0].ToString() + "\r\n";
                mbc.Disconnect();


            }
            catch (Exception ex)
            {
                label1.Text = ex.Message;
                mbc.Disconnect();

            }

             
        }

        // Read
        private void btnStop_Click(object sender, EventArgs e)
        {
            label1.Text = "";

            try
            {
                mbc.Connect();
               //  mbc.WriteSingleRegister(169, 0);
                int start =Int32.Parse(textBox5.Text);
                int[] results = mbc.ReadHoldingRegisters(start, 1);
                int idx = start;

                foreach(int i in results)
                {
                    textBox2.Text += "read: " + textBox5.Text + ": " + i.ToString() + "\r\n";
                    idx++;
                }
            }
            catch (Exception ex)
            {
                label1.Text = ex.Message;
            }
            finally
            {
               mbc.Disconnect();
            }
        }

        // Change Torquer
        private void button2_Click(object sender, EventArgs e)
        {
            label1.Text = "";
            try
            {
                mbc.Connect();
                mbc.WriteSingleRegister(8, Int32.Parse(textBox4.Text));
                mbc.WriteSingleRegister(9, Int32.Parse(textBox4.Text) * -1);

                label1.Text = "done";
                mbc.Disconnect();
            }
            catch (Exception ex)
            {
                label1.Text = ex.Message;
                mbc.Disconnect();

            }
        }

        private void txbDriverID_TextChanged(object sender, EventArgs e)
        {
            mbc.UnitIdentifier = Convert.ToByte(this.txbDriverID.Text);
        }

        private void button3_Click(object sender, EventArgs e)
        {


            var mbc = new ModbusClient("COM3")
            {

                Baudrate = 115200,
                UnitIdentifier = 4,
                StopBits = System.IO.Ports.StopBits.Two,
                Parity = System.IO.Ports.Parity.None

            };

            // Assuming you've already created and connected your ModbusClient:
            mbc.Connect();

            var servo = new AasdServoRs485(mbc);

            // One-time setup for RS-485 positioning via communication:
            servo.Init();

            // Move from 0 -> +1000 pulses:
            servo.MoveToPulses(Int32.Parse(txtPosition.Text), 300);

            // Optional: go back home later
            // servo.MoveHome();

            mbc.Disconnect();
        }
    }
}
