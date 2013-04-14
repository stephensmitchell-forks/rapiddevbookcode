﻿
<%@ Control Language="C#" AutoEventWireup="true" CodeFile="ListCustomerListInstancesReport.ascx.cs" Inherits="Controls_ListCustomerListInstancesReport" %>
<%@ Import Namespace="AW.Data"%>
<%@ Register Assembly="Microsoft.ReportViewer.WebForms"
    Namespace="Microsoft.Reporting.WebForms" TagPrefix="rsweb" %>

<asp:placeholder id="phHomeButton" runat="server" Visible="true">
	<asp:Button ID="btnHome" runat="server" Text="Home" OnClick="btnHome_Click" SkinID="ButtonSkin"/>
	&nbsp;&nbsp;&nbsp;
</asp:placeholder>
<llblgenpro:LLBLGenProDataSource ID="_CustomerListDS" runat="server" 
	DataContainerType="TypedList" 
	TypedListTypeName="AW.Data.TypedListClasses.CustomerListTypedList, AW.Data" 
	CacheLocation="Session" EnablePaging="True" />

<rsweb:ReportViewer ID="theReportViewer" runat="server" Width="100%"  SizeToReportContent="true">
    <LocalReport ReportPath= "Reports\\CustomerListReport.rdlc">
        <DataSources>
            <rsweb:ReportDataSource DataSourceId="_CustomerListDS" Name="CustomerList" />
        </DataSources>
    </LocalReport>
</rsweb:ReportViewer> 
