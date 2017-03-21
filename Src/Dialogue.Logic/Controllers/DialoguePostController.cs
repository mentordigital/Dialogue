﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Dialogue.Logic.Application;
using Dialogue.Logic.Application.Akismet;
using Dialogue.Logic.Constants;
using Dialogue.Logic.Mapping;
using Dialogue.Logic.Models;
using Dialogue.Logic.Models.ViewModels;
using Dialogue.Logic.Services;
using Umbraco.Core.Models;

namespace Dialogue.Logic.Controllers
{
    #region MVC Controllers
    public partial class DialoguePostSurfaceController : BaseSurfaceController
    {
        //private readonly IMemberGroup _membersGroup;
		private readonly List<IMemberGroup> _membersGroups;
		public DialoguePostSurfaceController()
        {
            //_membersGroup = (CurrentMember == null ? ServiceFactory.MemberService.GetGroupByName(AppConstants.GuestRoleName) : CurrentMember.Groups.FirstOrDefault());
			_membersGroups = new List<IMemberGroup>();
			if (CurrentMember == null)
			{
				_membersGroups.Add(ServiceFactory.MemberService.GetGroupByName(AppConstants.GuestRoleName));
			}
			else
			{
				_membersGroups = CurrentMember.Groups;
			}
		}

        [HttpPost]
        [Authorize]
        public void ApprovePost(ApprovePostViewModel model)
        {
            if (Request.IsAjaxRequest() && User.IsInRole(AppConstants.AdminRoleName))
            {
                using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
                {
                    var post = ServiceFactory.PostService.Get(model.Id);
                    post.Pending = false;
                    try
                    {
                        unitOfWork.Commit();
                    }
                    catch (Exception ex)
                    {
                        unitOfWork.Rollback();
                        LogError(ex);
                        throw ex;
                    }
                }
            }
        }

        [HttpPost]
        public ActionResult CreatePost(CreateAjaxPostViewModel post)
        {
            PermissionSet permissions;
            Post newPost;
            Topic topic;
            var postContent = string.Empty;
            using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
            {
                // Quick check to see if user is locked out, when logged in
                if (CurrentMember.IsLockedOut | !CurrentMember.IsApproved)
                {
                    ServiceFactory.MemberService.LogOff();
                    throw new Exception("No Access");
                }

                // Check for banned links
                if (ServiceFactory.BannedLinkService.ContainsBannedLink(post.PostContent))
                {
                    throw new Exception("Banned Link");
                }

                topic = ServiceFactory.TopicService.Get(post.Topic);

                postContent = ServiceFactory.BannedWordService.SanitiseBannedWords(post.PostContent);

                var akismetHelper = new AkismetHelper();

                newPost = ServiceFactory.PostService.AddNewPost(postContent, topic, CurrentMember, out permissions);

                if (!akismetHelper.IsSpam(newPost))
                {
                    try
                    {
                        unitOfWork.Commit();
                    }
                    catch (Exception ex)
                    {
                        unitOfWork.Rollback();
                        LogError(ex);
                        throw new Exception(Lang("Errors.GenericMessage"));
                    }
                }
                else
                {
                    unitOfWork.Rollback();
                    throw new Exception(Lang("Errors.PossibleSpam"));
                }
            }


			//get user post count > 5
			var currentMemberPostCount = ServiceFactory.PostService.GetByMember(CurrentMember.Id).Count();

			if (CurrentMember.Badges == null)
			{
				CurrentMember.Badges = ServiceFactory.BadgeService.GetallMembersBadges(CurrentMember.Id);
			}


			var hasBadge = CurrentMember.Badges != null && CurrentMember.Badges.Any(x => x.Name == "UserFivePost");

			//Check for moderation
			if (newPost.Pending || (currentMemberPostCount < 5 && !hasBadge))
            {
				// return PartialView(PathHelper.GetThemePartialViewPath("PostModeration"));
				NotifyCategoryAdmin(topic);
				return MessageToHomePage("Awaiting Moderation");
			}

            // All good send the notifications and send the post back
            using (UnitOfWorkManager.NewUnitOfWork())
            {

                // Create the view model
                var viewModel = PostMapper.MapPostViewModel(permissions, newPost, CurrentMember, Settings, topic, new List<Vote>(), new List<Favourite>());

                // Success send any notifications
                NotifyNewTopics(topic, postContent);


				var urlReferrer = Request.UrlReferrer;
				if (urlReferrer != null)
				{
					return Redirect(urlReferrer.AbsolutePath);
				}
				return PartialView(PathHelper.GetThemePartialViewPath("Post"), viewModel);
            }
        }

		private void NotifyCategoryAdmin(Topic topic)
		{
			var adminEmail = topic.Category.ModeratorEmailAddress;

			if (!string.IsNullOrEmpty(adminEmail))
			{
				var sb = new StringBuilder();
				sb.AppendFormat("<p>{0}</p>", string.Format("Topic ({0}) has new posts that need moderation", topic.Name));

				sb.AppendFormat("<p>{0}</p>", string.Concat(AppHelpers.ReturnCurrentDomain(), topic.Url));

				sb.AppendFormat("<p>Click below to access the authorise page (you may need to login)</p>");
				sb.AppendFormat("<p>{0}</p>",
				string.Concat(AppHelpers.ReturnCurrentDomain(),
				Urls.GenerateUrl(Urls.UrlType.Authorise)));

				var email = new Email
				{
					Body = ServiceFactory.EmailService.EmailTemplate(adminEmail, sb.ToString()),
					EmailFrom = Settings.NotificationReplyEmailAddress,
					EmailTo = adminEmail,
					NameTo = adminEmail,
					Subject = string.Format("{0} Subject", Settings.ForumName)
				};

				ServiceFactory.EmailService.SendMail(email);
			}
		}

		private void NotifyNewTopics(Topic topic, string postContent)
        {
            // Get all notifications for this category
            var notifications = ServiceFactory.TopicNotificationService.GetByTopic(topic).Select(x => x.MemberId).ToList();

            if (notifications.Any())
            {
                // remove the current user from the notification, don't want to notify yourself that you 
                // have just made a topic!
                notifications.Remove(CurrentMember.Id);

                if (notifications.Count > 0)
                {
                    // Now get all the users that need notifying
                    var usersToNotify = ServiceFactory.MemberService.GetUsersById(notifications);

                    // Create the email
                    var sb = new StringBuilder();
                    sb.AppendFormat("<p>{0}</p>", string.Format("{0} New Posts", topic.Name));
                    sb.AppendFormat("<p>{0}</p>", string.Concat(Settings.ForumRootUrlWithDomain.TrimEnd('/'), topic.Url));
                    sb.Append(postContent);

                    // create the emails only to people who haven't had notifications disabled
                    var emails = usersToNotify.Where(x => x.DisableEmailNotifications != true).Select(user => new Email
                    {
                        Body = ServiceFactory.EmailService.EmailTemplate(user.UserName, sb.ToString()),
                        EmailFrom = Settings.NotificationReplyEmailAddress,
                        EmailTo = user.Email,
                        NameTo = user.UserName,
                        Subject = string.Format("{0} Subject", Settings.ForumName)
                    }).ToList();

                    // and now pass the emails in to be sent
                    ServiceFactory.EmailService.SendMail(emails);
                }
            }
        }

        public ActionResult DeletePost(Guid id)
        {
            bool isTopicStarter;
            Topic topic;

            using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
            {
                // Got to get a lot of things here as we have to check permissions
                // Get the post
                var post = ServiceFactory.PostService.Get(id);

                // get this so we know where to redirect after
                isTopicStarter = post.IsTopicStarter;

                // Get the topic
                topic = post.Topic;
                var category = ServiceFactory.CategoryService.Get(topic.CategoryId);

                // get the users permissions
                var permissions = ServiceFactory.PermissionService.GetPermissions(category, _membersGroups);

                if (post.MemberId == CurrentMember.Id || permissions[AppConstants.PermissionModerate].IsTicked)
                {
                    var postUser = post.Member;

                    var deleteTopic = ServiceFactory.PostService.Delete(post);
                    unitOfWork.SaveChanges();

                    if (deleteTopic)
                    {
                        ServiceFactory.TopicService.Delete(topic);
                    }

                    // Remove the points the user got for this post
                    ServiceFactory.MemberPointsService.Delete(Settings.PointsAddedPerNewPost, postUser);

                    try
                    {
                        // Commit changes
                        unitOfWork.Commit();
                    }
                    catch (Exception ex)
                    {
                        unitOfWork.Rollback();
                        LogError(ex);
                        throw new Exception(Lang("Errors.GenericMessage"));
                    }
                }
            }

            // Deleted successfully
            if (isTopicStarter)
            {
                // Redirect to root as this was a topic and deleted
                TempData[AppConstants.MessageViewBagName] = new GenericMessageViewModel
                {
                    Message = "Topic Deleted",
                    MessageType = GenericMessages.Success
                };
                return Redirect(Settings.ForumRootUrl);
            }

            // Show message that post is deleted
            TempData[AppConstants.MessageViewBagName] = new GenericMessageViewModel
            {
                Message = "Post Deleted",
                MessageType = GenericMessages.Success
            };

            return Redirect(topic.Url);
        }


        [HttpPost]
        [Authorize]
        public ActionResult Report(ReportPostViewModel viewModel)
        {
            if (Settings.EnableSpamReporting)
            {
                using (UnitOfWorkManager.NewUnitOfWork())
                {
                    var post = ServiceFactory.PostService.Get(viewModel.PostId);

                    // Banned link?
                    if (ServiceFactory.BannedLinkService.ContainsBannedLink(viewModel.Reason))
                    {
                        ShowMessage(new GenericMessageViewModel
                        {
                            Message = Lang("Errors.BannedLink"),
                            MessageType = GenericMessages.Danger
                        });
                        return Redirect(post.Topic.Url);
                    }

                    var report = new Report
                    {
                        Reason = viewModel.Reason,
                        ReportedPost = post,
                        Reporter = CurrentMember
                    };
                    ServiceFactory.ReportService.PostReport(report);

                    var message= new GenericMessageViewModel
                    {
                        Message = Lang("Report.ReportSent"),
                        MessageType = GenericMessages.Success
                    };
                    ShowMessage(message);
                    return Redirect(post.Topic.Url);
                }
            }
            return ErrorToHomePage(Lang("Errors.GenericMessage"));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditPost(EditPostViewModel editPostViewModel)
        {

            using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
            {
                // Got to get a lot of things here as we have to check permissions
                // Get the post
                var post = ServiceFactory.PostService.Get(editPostViewModel.Id);

                // Get the topic
                var topic = post.Topic;
                var category = ServiceFactory.CategoryService.Get(topic.CategoryId);
                topic.Category = category;

                // get the users permissions
                var permissions = ServiceFactory.PermissionService.GetPermissions(category, _membersGroups);

                if (post.MemberId == CurrentMember.Id || permissions[AppConstants.PermissionModerate].IsTicked)
                {
                    // User has permission so update the post
                    post.PostContent = AppHelpers.GetSafeHtml(ServiceFactory.BannedWordService.SanitiseBannedWords(editPostViewModel.Content));
                    post.DateEdited = DateTime.UtcNow;

                    // if topic starter update the topic
                    if (post.IsTopicStarter)
                    {
                        // if category has changed then update it
                        if (topic.Category.Id != editPostViewModel.Category)
                        {
                            var cat = ServiceFactory.CategoryService.Get(editPostViewModel.Category);
                            topic.Category = cat;
                        }

                        topic.IsLocked = editPostViewModel.IsLocked;
                        topic.IsSticky = editPostViewModel.IsSticky;
                        topic.Name = AppHelpers.GetSafeHtml(ServiceFactory.BannedWordService.SanitiseBannedWords(editPostViewModel.Name));

                        // See if there is a poll
                        if (editPostViewModel.PollAnswers != null && editPostViewModel.PollAnswers.Count > 0)
                        {
                            // Now sort the poll answers, what to add and what to remove
                            // Poll answers already in this poll.
                            var postedIds = editPostViewModel.PollAnswers.Select(x => x.Id);
                            //var existingAnswers = topic.Poll.PollAnswers.Where(x => postedIds.Contains(x.Id)).ToList();
                            var existingAnswers = editPostViewModel.PollAnswers.Where(x => topic.Poll.PollAnswers.Select(p => p.Id).Contains(x.Id)).ToList();
                            var newPollAnswers = editPostViewModel.PollAnswers.Where(x => !topic.Poll.PollAnswers.Select(p => p.Id).Contains(x.Id)).ToList();
                            var pollAnswersToRemove = topic.Poll.PollAnswers.Where(x => !postedIds.Contains(x.Id)).ToList();

                            // Loop through existing and update names if need be
                            //TODO: Need to think about this in future versions if they change the name
                            //TODO: As they could game the system by getting votes and changing name?
                            foreach (var existPollAnswer in existingAnswers)
                            {
                                // Get the existing answer from the current topic
                                var pa = topic.Poll.PollAnswers.FirstOrDefault(x => x.Id == existPollAnswer.Id);
                                if (pa != null && pa.Answer != existPollAnswer.Answer)
                                {
                                    // If the answer has changed then update it
                                    pa.Answer = existPollAnswer.Answer;
                                }
                            }

                            // Loop through and remove the old poll answers and delete
                            foreach (var oldPollAnswer in pollAnswersToRemove)
                            {
                                // Delete
                                ServiceFactory.PollService.Delete(oldPollAnswer);

                                // Remove from Poll
                                topic.Poll.PollAnswers.Remove(oldPollAnswer);
                            }

                            // Poll answers to add
                            foreach (var newPollAnswer in newPollAnswers)
                            {
                                var npa = new PollAnswer
                                {
                                    Poll = topic.Poll,
                                    Answer = newPollAnswer.Answer
                                };
                                ServiceFactory.PollService.Add(npa);
                                topic.Poll.PollAnswers.Add(npa);
                            }
                        }
                        else
                        {
                            // Need to check if this topic has a poll, because if it does
                            // All the answers have now been removed so remove the poll.
                            if (topic.Poll != null)
                            {
                                //Firstly remove the answers if there are any
                                if (topic.Poll.PollAnswers != null && topic.Poll.PollAnswers.Any())
                                {
                                    var answersToDelete = new List<PollAnswer>();
                                    answersToDelete.AddRange(topic.Poll.PollAnswers);
                                    foreach (var answer in answersToDelete)
                                    {
                                        // Delete
                                        ServiceFactory.PollService.Delete(answer);

                                        // Remove from Poll
                                        topic.Poll.PollAnswers.Remove(answer);
                                    }
                                }

                                // Now delete the poll
                                var pollToDelete = topic.Poll;
                                ServiceFactory.PollService.Delete(pollToDelete);

                                // Remove from topic.
                                topic.Poll = null;
                            }
                        }
                    }

                    // redirect back to topic
                    var message = new GenericMessageViewModel
                    {
                        Message = "Post Updated",
                        MessageType = GenericMessages.Success
                    };
                    try
                    {
                        unitOfWork.Commit();
                        ShowMessage(message);
                        return Redirect(topic.Url);
                    }
                    catch (Exception ex)
                    {
                        unitOfWork.Rollback();
                        LogError(ex);
                        throw new Exception(Lang("Errors.GenericError"));
                    }
                }

                return NoPermission(topic);
            }
        }
    } 
    #endregion
}