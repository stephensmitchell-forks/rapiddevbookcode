#region Copyright (c) 2003-2005, Luke T. Maxon

/********************************************************************************************************************
'
' Copyright (c) 2003-2005, Luke T. Maxon
' All rights reserved.
' 
' Redistribution and use in source and binary forms, with or without modification, are permitted provided
' that the following conditions are met:
' 
' * Redistributions of source code must retain the above copyright notice, this list of conditions and the
' 	following disclaimer.
' 
' * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and
' 	the following disclaimer in the documentation and/or other materials provided with the distribution.
' 
' * Neither the name of the author nor the names of its contributors may be used to endorse or 
' 	promote products derived from this software without specific prior written permission.
' 
' THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED
' WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
' PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
' ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
' LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
' INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
' OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN
' IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
'
'*******************************************************************************************************************/

#endregion

using NUnit.Framework;

namespace NUnit.Extensions.Forms.TestApplications
{
    ///<summary>
    /// Test Fixture for the <see cref="RichTextBoxTester"/> class.
    ///</summary>
    [TestFixture]
    public class RichTextBoxTest : NUnitFormTest
    {
        public void oldhandler(string name, System.IntPtr hWnd, System.Windows.Forms.Form form)
        {
            MessageBoxTester mb = new MessageBoxTester(hWnd);
            Assert.AreEqual("Old", mb.Text);
            mb.ClickOk();
        }

        public void newhandler(string name, System.IntPtr hWnd, System.Windows.Forms.Form form)
        {
            MessageBoxTester mb = new MessageBoxTester(hWnd);
            Assert.AreEqual("New", mb.Text);
            mb.ClickOk();
        }

        [Test]
        public void DataSetBinding()
        {
            RichTextBoxDataSetBindingTestForm f = new RichTextBoxDataSetBindingTestForm();
            f.Show();
            ModalFormHandler = oldhandler;
            new ButtonTester("btnView").Click();
            new RichTextBoxTester("myRichTextBox").Enter("New");
            ModalFormHandler = newhandler;
            new ButtonTester("btnView").Click();
            f.Close();
        }

        [Test]
        public void DataSetBindingWithGenericPropertySetter()
        {
            RichTextBoxDataSetBindingTestForm f = new RichTextBoxDataSetBindingTestForm();
            f.Show();

            ModalFormHandler = oldhandler;
            new ButtonTester("btnView").Click();

            new RichTextBoxTester("myRichTextBox")["Text"] = "New";

            ModalFormHandler = newhandler;
            new ButtonTester("btnView").Click();
            f.Close();
        }

        [Test]
        public void RichTextBox()
        {
            RichTextBoxTestForm f = new RichTextBoxTestForm();
            f.Show();
            RichTextBoxTester box = new RichTextBoxTester("myTextBox");
            Assert.AreEqual("default", box.Text);
            box.Enter("Text");
            Assert.AreEqual("Text", box.Text);
            f.Close();
        }
    }
}