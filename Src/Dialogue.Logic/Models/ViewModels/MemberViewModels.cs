using System;
using System.ComponentModel.DataAnnotations;
using System.Web;
using System.Web.Mvc;
using Dialogue.Logic.Application;
using Umbraco.Core.Models;

namespace Dialogue.Logic.Models.ViewModels
{
    public class ApproveMemberViewModel
    {
        public int Id { get; set; }
    }

    public class RegisterViewModel
    {
        [Required]
        [DialogueDisplayName("Username")]
        public string UserName { get; set; }

        [Required]
        [DataType(DataType.EmailAddress)]
        [DialogueDisplayName("Email Address")]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [DialogueDisplayName("Password ")]
        public string Password { get; set; }

        public string SpamAnswer { get; set; }
        public int? ForumId { get; set; }
        public string ReturnUrl { get; set; }
        public string SocialProfileImageUrl { get; set; }
        public string UserAccessToken { get; set; }
        public LoginType LoginType { get; set; }
    }

    public class PageReportMemberViewModel : MasterModel
    {
        public PageReportMemberViewModel(IPublishedContent content) : base(content)
        {
        }

        public int MemberId { get; set; }
        public string Username { get; set; }
        public string Reason { get; set; }
    }

    public class ReportMemberViewModel
    {
        public int MemberId { get; set; }
        public string Username { get; set; }
        public string Reason { get; set; }
    }

    public class PageMemberEditViewModel : MasterModel
    {
        public PageMemberEditViewModel(IPublishedContent content) : base(content)
        {
        }

        public MemberEditViewModel MemberEditViewModel { get; set; }
    }

    public class PostMemberEditViewModel
    {
        public MemberEditViewModel MemberEditViewModel { get; set; }
    }

    public class MemberEditViewModel
    {
        [Required]
        public int Id { get; set; }

        [Required]
        [DialogueDisplayName("Username")]
        [StringLength(150, MinimumLength = 2)]
        public string UserName { get; set; }

        [DialogueDisplayName("Email Address")]
        [DataType(DataType.EmailAddress)]
        [Required]
        public string Email { get; set; }

        [DialogueDisplayName("Signature")]
        [StringLength(1000)]
        [AllowHtml]
        public string Signature { get; set; }

        [DialogueDisplayName("Website")]
        [Url]
        [StringLength(100)]
        public string Website { get; set; }

        [DialogueDisplayName("Upload New Avatar")]
        public HttpPostedFileBase[] Files { get; set; }

        public string Avatar { get; set; }

        [DialogueDisplayName("Twitter")]
        public string Twitter { get; set; }

        // Admin Stuff

        [DialogueDisplayName("Disable Email Notifications")]
        public bool DisableEmailNotifications { get; set; }

        [DialogueDisplayName("Disable Posting")]
        public bool DisablePosting { get; set; }

        [DialogueDisplayName("Disable Private Messages")]
        public bool DisablePrivateMessages { get; set; }

        [DialogueDisplayName("Disable File Uploads")]
        public bool DisableFileUploads { get; set; }

        [DialogueDisplayName("Can Edit Other Members")]
        public bool CanEditOtherMembers { get; set; }

        [DialogueDisplayName("Comment")]
        public string Comments { get; set; }
    }

    public class PageChangePasswordViewModel : MasterModel
    {
        public PageChangePasswordViewModel(IPublishedContent content) : base(content)
        {
        }

        public ChangePasswordViewModel ChangePasswordViewModel { get; set; }
    }

    public class PostChangePasswordViewModel
    {
        public ChangePasswordViewModel ChangePasswordViewModel { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [DialogueDisplayName("Current Password")]
        public string OldPassword { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        [DialogueDisplayName("New Password")]
        public string NewPassword { get; set; }

        [DataType(DataType.Password)]
        [DialogueDisplayName("Confirm New Password")]
        [System.ComponentModel.DataAnnotations.Compare("NewPassword")]
        public string ConfirmPassword { get; set; }
    }

}