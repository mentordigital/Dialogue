﻿using System;
using System.Linq;
using System.Web.Mvc;
using Dialogue.Logic.Application;
using Dialogue.Logic.Application.Akismet;
using Dialogue.Logic.Constants;
using Dialogue.Logic.Mapping;
using Dialogue.Logic.Models;
using Dialogue.Logic.Models.ViewModels;
using Dialogue.Logic.Routes;
using Dialogue.Logic.Services;
using Umbraco.Core.Models;
using Umbraco.Web.Models;
using System.Collections.Generic;
using System.Text;

namespace Dialogue.Logic.Controllers
{
    #region Render Controllers
    public partial class DialogueTopicController : BaseRenderController
    {
       // private readonly IMemberGroup _membersGroup;
		private readonly List<IMemberGroup> _membersGroups;
		public DialogueTopicController()
        {
           // _membersGroup = (CurrentMember == null ? ServiceFactory.MemberService.GetGroupByName(AppConstants.GuestRoleName) : CurrentMember.Groups.FirstOrDefault());
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

        /// <summary>
        /// Used to render the Topic (virtual node)
        /// </summary>
        /// <param name="model"></param>
        /// <param name="topicname">
        /// The topic slug which we use to look up the topic
        /// </param>
        /// <param name="p"></param>
        /// <returns></returns>
        public ActionResult Show(RenderModel model, string topicname, int? p = null)
        {
            var tagPage = model.Content as DialogueVirtualPage;
            if (tagPage == null)
            {
                throw new InvalidOperationException("The RenderModel.Content instance must be of type " + typeof(DialogueVirtualPage));
            }

            if (string.IsNullOrEmpty(topicname))
            {
                return ErrorToHomePage("Please enter a Topic name");
            }

            // Set the page index
            var pageIndex = AppHelpers.ReturnCurrentPagingNo();

            using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
            {
                // Get the topic
                var topic = ServiceFactory.TopicService.GetTopicBySlug(topicname);

                if (topic != null)
                {
                    // Note: Don't use topic.Posts as its not a very efficient SQL statement
                    // Use the post service to get them as it includes other used entities in one
                    // statement rather than loads of sql selects

                    var sortQuerystring = Request.QueryString[AppConstants.PostOrderBy];
                    var orderBy = !string.IsNullOrEmpty(sortQuerystring) ?
                                              AppHelpers.EnumUtils.ReturnEnumValueFromString<PostOrderBy>(sortQuerystring) : PostOrderBy.Standard;

                    // Store the amount per page
                    var amountPerPage = Settings.PostsPerPage;
                    var hasCommentHash = Request.Url != null &&
                                         Request.Url.PathAndQuery.IndexOf("#comment-",
                                             StringComparison.CurrentCultureIgnoreCase) >= 0;

                    if (sortQuerystring == PostOrderBy.All.ToString() || hasCommentHash)
                    {
                        // Overide to show all posts
                        amountPerPage = int.MaxValue;
                    }


                    // Get the posts
                    var posts = ServiceFactory.PostService.GetPagedPostsByTopic(pageIndex,
                                                                  amountPerPage,
                                                                  int.MaxValue,
                                                                  topic.Id,
                                                                  orderBy);

                    // Get the permissions for the category that this topic is in
                    var permissions = ServiceFactory.PermissionService.GetPermissions(topic.Category, _membersGroups);

                    // If this user doesn't have access to this topic then
                    // redirect with message
                    if (permissions[AppConstants.PermissionDenyAccess].IsTicked)
                    {
                        return ErrorToHomePage("No Permission");
                    }

                    // See if the user has subscribed to this topic or not
                    var isSubscribed = UserIsAuthenticated && (ServiceFactory.TopicNotificationService.GetByUserAndTopic(CurrentMember, topic).Any());

                    // Populate the view model for this page
                    var viewModel = new ShowTopicViewModel(model.Content)
                    {
                        Topic = topic,
                        PageIndex = posts.PageIndex,
                        TotalCount = posts.TotalCount,
                        Permissions = permissions,
                        User = CurrentMember,
                        IsSubscribed = isSubscribed,
                        UserHasAlreadyVotedInPoll = false,
                        TotalPages = posts.TotalPages
                    };

                    // Get all votes for all the posts
                    var postIds = posts.Select(x => x.Id).ToList();
                    var allPostVotes = ServiceFactory.VoteService.GetAllVotesForPosts(postIds);

                    // Get all favourites for this user
                    viewModel.Favourites = new List<Favourite>();
                    if (CurrentMember != null)
                    {
                        viewModel.Favourites.AddRange(ServiceFactory.FavouriteService.GetAllByMember(CurrentMember.Id));
                    }

                    // Map the topic Start
                    // Get the topic starter post
                    var topicStarter = ServiceFactory.PostService.GetTopicStarterPost(topic.Id);
                    viewModel.TopicStarterPost = PostMapper.MapPostViewModel(permissions, topicStarter, CurrentMember, Settings, topic, topicStarter.Votes.ToList(), viewModel.Favourites);

                    // Map the posts to the posts viewmodel
                    viewModel.Posts = new List<ViewPostViewModel>();
                    foreach (var post in posts)
                    {
                        var postViewModel = PostMapper.MapPostViewModel(permissions, post, CurrentMember, Settings, topic, allPostVotes, viewModel.Favourites);
                        viewModel.Posts.Add(postViewModel);
                    }

                    // If there is a quote querystring
                    var quote = Request["quote"];
                    if (!string.IsNullOrEmpty(quote))
                    {
                        try
                        {
                            // Got a quote
                            var postToQuote = ServiceFactory.PostService.Get(new Guid(quote));
                            viewModel.PostContent = postToQuote.PostContent;
                        }
                        catch (Exception ex)
                        {
                            LogError(ex);
                        }
                    }

                    // See if the topic has a poll, and if so see if this user viewing has already voted
                    if (topic.Poll != null)
                    {
                        // There is a poll and a user
                        // see if the user has voted or not
                        var votes = topic.Poll.PollAnswers.SelectMany(x => x.PollVotes).ToList();
                        if (UserIsAuthenticated)
                        {
                            viewModel.UserHasAlreadyVotedInPoll = (votes.Count(x => x.MemberId == CurrentMember.Id) > 0);
                        }
                        viewModel.TotalVotesInPoll = votes.Count();
                    }

                    // update the topic view count only if this topic doesn't belong to the user looking at it
                    var addView = true;
                    if (UserIsAuthenticated && CurrentMember.Id != topic.MemberId)
                    {
                        addView = false;
                    }

                    if (!AppHelpers.UserIsBot() && addView)
                    {
                        // Cool, user doesn't own this topic
                        topic.Views = (topic.Views + 1);
                        try
                        {
                            unitOfWork.Commit();
                        }
                        catch (Exception ex)
                        {
                            LogError(ex);
                        }
                    }

                    return View(PathHelper.GetThemeViewPath("Topic"), viewModel);
                }

            }
            return ErrorToHomePage("Something went wrong. Please try again");
        }

    }

    #endregion

    #region Surface controllers
    public partial class DialogueTopicSurfaceController : BaseSurfaceController
    {

        //private readonly IMemberGroup _membersGroup;
		private readonly List<IMemberGroup> _membersGroups;
		public DialogueTopicSurfaceController()
        {
           // _membersGroup = (CurrentMember == null ? ServiceFactory.MemberService.GetGroupByName(AppConstants.GuestRoleName) : CurrentMember.Groups.FirstOrDefault());
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
        public void ApproveTopic(ApproveTopicViewModel model)
        {
            if (Request.IsAjaxRequest() && User.IsInRole(AppConstants.AdminRoleName))
            {
                using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
                {
                    var topic = ServiceFactory.TopicService.Get(model.Id);
                    topic.Pending = false;
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
        public PartialViewResult AjaxMorePosts(GetMorePostsViewModel getMorePostsViewModel)
        {
            // Get the topic
            var topic = ServiceFactory.TopicService.Get(getMorePostsViewModel.TopicId);

            // Get the permissions for the category that this topic is in
            var permissions = ServiceFactory.PermissionService.GetPermissions(topic.Category, _membersGroups);

            // If this user doesn't have access to this topic then just return nothing
            if (permissions[AppConstants.PermissionDenyAccess].IsTicked)
            {
                return null;
            }

            var orderBy = !string.IsNullOrEmpty(getMorePostsViewModel.Order) ?
                                      AppHelpers.EnumUtils.ReturnEnumValueFromString<PostOrderBy>(getMorePostsViewModel.Order) : PostOrderBy.Standard;



            var viewModel = new ShowMorePostsViewModel
            {
                Topic = topic,
                Permissions = permissions,
                User = CurrentMember
            };

            // Map the posts to the posts viewmodel

            // Get all favourites for this user
            var favourites = new List<Favourite>();
            if (CurrentMember != null)
            {
                favourites.AddRange(ServiceFactory.FavouriteService.GetAllByMember(CurrentMember.Id));
            }

            // Get the posts
            var posts = ServiceFactory.PostService.GetPagedPostsByTopic(getMorePostsViewModel.PageIndex, Settings.PostsPerPage, int.MaxValue, topic.Id, orderBy);

            // Get all votes for all the posts
            var postIds = posts.Select(x => x.Id).ToList();
            var allPostVotes = ServiceFactory.VoteService.GetAllVotesForPosts(postIds);

            viewModel.Posts = new List<ViewPostViewModel>();
            foreach (var post in posts)
            {
                var postViewModel = PostMapper.MapPostViewModel(permissions, post, CurrentMember, Settings, topic, allPostVotes, favourites);
                viewModel.Posts.Add(postViewModel);
            }

            return PartialView(PathHelper.GetThemePartialViewPath("AjaxMorePosts"), viewModel);
        }

        [ChildActionOnly]
        public PartialViewResult GetTopicBreadcrumb(Topic topic)
        {
            var category = ServiceFactory.CategoryService.Get(topic.CategoryId, true);
            var viewModel = new BreadCrumbViewModel
            {
                Categories = category.ParentCategories,
                Topic = topic
            };
            if (!viewModel.Categories.Any())
            {
                viewModel.Categories.Add(topic.Category);
            }
            return PartialView(PathHelper.GetThemePartialViewPath("GetTopicBreadcrumb"), viewModel);
        }

        public PartialViewResult LatestTopics(int? p)
        {
            using (UnitOfWorkManager.NewUnitOfWork())
            {
                // Set the page index
                var pageIndex = p ?? 1;

                // Get the topics
                var topics = ServiceFactory.TopicService.GetRecentTopics(pageIndex,
                                                           Dialogue.Settings().TopicsPerPage,
                                                           AppConstants.ActiveTopicsListSize);

                // Get all the categories for this topic collection
                var categories = topics.Select(x => x.Category).Distinct();

                // create the view model
                var viewModel = new ActiveTopicsViewModel
                {
                    Topics = topics,
                    AllPermissionSets = new Dictionary<Category, PermissionSet>(),
                    PageIndex = pageIndex,
                    TotalCount = topics.TotalCount,
                    User = CurrentMember
                };

                // loop through the categories and get the permissions
                foreach (var category in categories)
                {
                    var permissionSet = ServiceFactory.PermissionService.GetPermissions(category, _membersGroups);
                    viewModel.AllPermissionSets.Add(category, permissionSet);
                }
                return PartialView(PathHelper.GetThemePartialViewPath("LatestTopics"), viewModel);
            }
        }


        //// POST: /Customer/Create
        //// To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        //// more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<ActionResult> Create([Bind(Include = "CustomerID,CompanyName,ContactName,ContactTitle,Address,City,Region,PostalCode,Country,Phone,Fax")] Customer customer)
        //{
        //    if (ModelState.IsValid)
        //    {
        //        _customerService.Add(customer);
        //        await _unitOfWork.SaveAsync();
        //        return RedirectToAction("Index");
        //    }

        //    return View(customer);
        //}


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CreateTopicViewModel topicViewModel)
        {
            if (ModelState.IsValid)
            {
                // Quick check to see if user is locked out, when logged in
                if (CurrentMember.IsLockedOut || CurrentMember.DisablePosting == true || !CurrentMember.IsApproved)
                {
                    ServiceFactory.MemberService.LogOff();
                    return ErrorToHomePage("No Permission");
                }

                var successfullyCreated = false;
                var moderate = false;
                Category category;
                var topic = new Topic();

                using (var unitOfWork = UnitOfWorkManager.NewUnitOfWork())
                {
                    // Before we do anything DB wise, check it contains no bad links
                    if (ServiceFactory.BannedLinkService.ContainsBannedLink(topicViewModel.TopicContent))
                    {
                        ShowMessage(new GenericMessageViewModel
                        {
                            Message = Lang("Errors.BannedLink"),
                            MessageType = GenericMessages.Danger
                        });
                        return Redirect(Urls.GenerateUrl(Urls.UrlType.TopicCreate));
                    }

                    // Not using automapper for this one only, as a topic is a post and topic in one
                    category = ServiceFactory.CategoryService.Get(topicViewModel.Category);

                    // First check this user is allowed to create topics in this category
                    var permissions = ServiceFactory.PermissionService.GetPermissions(category, _membersGroups);

                    // Check this users role has permission to create a post
                    if (permissions[AppConstants.PermissionDenyAccess].IsTicked || permissions[AppConstants.PermissionReadOnly].IsTicked || !permissions[AppConstants.PermissionCreateTopics].IsTicked)
                    {
                        // Throw exception so Ajax caller picks it up
                        ModelState.AddModelError(string.Empty, "No Permission");
                    }
                    else
                    {
                        // We get the banned words here and pass them in, so its just one call
                        // instead of calling it several times and each call getting all the words back
                        

                        topic = new Topic
                        {
                            Name = ServiceFactory.BannedWordService.SanitiseBannedWords(topicViewModel.TopicName, Dialogue.Settings().BannedWords),
                            Category = category,
                            CategoryId = category.Id,
                            Member = CurrentMember,
                            MemberId = CurrentMember.Id
                        };

                        // See if the user has actually added some content to the topic
                        if (!string.IsNullOrEmpty(topicViewModel.TopicContent))
                        {
                            // Check for any banned words
                            topicViewModel.TopicContent = ServiceFactory.BannedWordService.SanitiseBannedWords(topicViewModel.TopicContent, Dialogue.Settings().BannedWords);

                            // See if this is a poll and add it to the topic
                            if (topicViewModel.PollAnswers != null && topicViewModel.PollAnswers.Count > 0)
                            {
                                // Do they have permission to create a new poll
                                if (permissions[AppConstants.PermissionCreatePolls].IsTicked)
                                {
                                    // Create a new Poll
                                    var newPoll = new Poll
                                    {
                                        Member = CurrentMember,
                                        MemberId = CurrentMember.Id
                                    };

                                    // Create the poll
                                    ServiceFactory.PollService.Add(newPoll);

                                    // Save the poll in the context so we can add answers
                                    unitOfWork.SaveChanges();

                                    // Now sort the answers
                                    var newPollAnswers = new List<PollAnswer>();
                                    foreach (var pollAnswer in topicViewModel.PollAnswers)
                                    {
                                        // Attach newly created poll to each answer
                                        pollAnswer.Poll = newPoll;
                                        ServiceFactory.PollService.Add(pollAnswer);
                                        newPollAnswers.Add(pollAnswer);
                                    }
                                    // Attach answers to poll
                                    newPoll.PollAnswers = newPollAnswers;

                                    // Save the new answers in the context
                                    unitOfWork.SaveChanges();

                                    // Add the poll to the topic
                                    topic.Poll = newPoll;
                                }
                                else
                                {
                                    //No permission to create a Poll so show a message but create the topic
                                    ShowMessage(new GenericMessageViewModel
                                    {
                                        Message = Lang("No PermissionPolls"),
                                        MessageType = GenericMessages.Info
                                    });
                                }
                            }


							//get user post count > 5
							var currentMemberPostCount = ServiceFactory.PostService.GetByMember(CurrentMember.Id).Count();

							if (CurrentMember.Badges == null)
							{
								CurrentMember.Badges = ServiceFactory.BadgeService.GetallMembersBadges(CurrentMember.Id);
							}

							var hasBadge = CurrentMember.Badges != null && CurrentMember.Badges.Any(x => x.Name == "UserFivePost");
							

							// Check for moderation
							if (category.ModerateAllTopicsInThisCategory || (currentMemberPostCount < 5 && !hasBadge))
                            {
								NotifyCategoryAdmin(topic);
								topic.Pending = true;
                                moderate = true;
                            }


                            // Create the topic
                            topic = ServiceFactory.TopicService.Add(topic);

                            // Save the changes
                            unitOfWork.SaveChanges();

                            // Now create and add the post to the topic
                            ServiceFactory.TopicService.AddLastPost(topic, topicViewModel.TopicContent);

                            // Update the users points score for posting
                            ServiceFactory.MemberPointsService.Add(new MemberPoints
                            {
                                Points = Settings.PointsAddedPerNewPost,
                                Member = CurrentMember,
                                MemberId = CurrentMember.Id,
                                RelatedPostId = topic.LastPost.Id
                            });

                            // Now check its not spam
                            var akismetHelper = new AkismetHelper();
                            if (akismetHelper.IsSpam(topic))
                            {
                                // Could be spam, mark as pending
                                topic.Pending = true;
                            }

                            // Subscribe the user to the topic as they have checked the checkbox
                            if (topicViewModel.SubscribeToTopic)
                            {
                                // Create the notification
                                var topicNotification = new TopicNotification
                                {
                                    Topic = topic,
                                    Member = CurrentMember,
                                    MemberId = CurrentMember.Id
                                };
                                //save
                                ServiceFactory.TopicNotificationService.Add(topicNotification);
                            }

                            try
                            {
                                unitOfWork.Commit();
                                if (!moderate)
                                {
                                    successfullyCreated = true;
                                }

                                // Update the users post count
                                ServiceFactory.MemberService.AddPostCount(CurrentMember);

                            }
                            catch (Exception ex)
                            {
                                unitOfWork.Rollback();
                                LogError(ex);
                                ModelState.AddModelError(string.Empty, "Something went wrong. Please try again");
                            }
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty, "Please enter some content");
                        }
                    }
                }

                using (UnitOfWorkManager.NewUnitOfWork())
                {
                    if (successfullyCreated)
                    {
						//TODO: programtically add topic guid to page forum tab properties
						if (topicViewModel.PageId > 0)
						{
							var nodeId = topicViewModel.PageId;
							var node = ApplicationContext.Services.ContentService.GetPublishedVersion(nodeId);
							if (node != null)
							{
								var topicPickerValue = node.GetValue("topicPicker");

								if (topicPickerValue != null)
								{
									var documentTopics = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(topicPickerValue.ToString()).ToList();
									documentTopics.Add(topic.Id.ToString());

									string[] newTopics = documentTopics.Select(x => x).ToArray();
									string topicsJson = Newtonsoft.Json.JsonConvert.SerializeObject(newTopics);

									node.SetValue("topicPicker", topicsJson);
									ApplicationContext.Services.ContentService.Save(node);
									ApplicationContext.Services.ContentService.Publish(node);
								}
							}
						}



						// Success so now send the emails
						NotifyNewTopics(category);

                        // Redirect to the newly created topic
                        return Redirect(string.Format("{0}?postbadges=true", topic.Url));
                    }
                    if (moderate)
                    {
                        // Moderation needed
                        // Tell the user the topic is awaiting moderation
                        return MessageToHomePage("Awaiting Moderation");
                    }
                }
            }
            ShowModelErrors();
            return Redirect(Urls.GenerateUrl(Urls.UrlType.TopicCreate));
        }

		private void NotifyCategoryAdmin(Topic topic)
		{
			var adminEmail = topic.Category.ModeratorEmailAddress;

			if(!string.IsNullOrEmpty(adminEmail))
			{
				var sb = new StringBuilder();
				sb.AppendFormat("<p>{0}</p>", string.Format("New topic in {0} that needs moderation ({1})", topic.Category.Name, topic.Name));
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

        private void NotifyNewTopics(Category cat)
        {
            // *CHANGE THIS TO BE CALLED LIKE THE BADGES VIA AN AJAX Method* 
            // TODO: This really needs to be an async call so it doesn't hang when a user creates  
            //  a topic if there are 1000's of users

            // Get all notifications for this category
            var notifications = ServiceFactory.CategoryNotificationService.GetByCategory(cat).Select(x => x.MemberId).ToList();

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
                    sb.AppendFormat("<p>{0}</p>", string.Format("{0} New Topics", cat.Name));
                    sb.AppendFormat("<p>{0}</p>", string.Concat(Settings.ForumRootUrlWithDomain.TrimEnd('/'), cat.Url));

                    // create the emails and only send them to people who have not had notifications disabled
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


        public PartialViewResult CreateTopicButton(int pageId = -1)
        {
			var viewModel = new CreateTopicButtonViewModel
			{
				LoggedOnUser = CurrentMember,
				CategoryId = 0,
				PageId = pageId
            };

            if (CurrentMember != null)
            {
                // Add all categories to a permission set
                var allCategories = ServiceFactory.CategoryService.GetAll();
                using (UnitOfWorkManager.NewUnitOfWork())
                {
                    foreach (var category in allCategories)
                    {
                        // Now check to see if they have access to any categories
                        // if so, check they are allowed to create topics - If no to either set to false
                        viewModel.UserCanPostTopics = false;
                        var permissionSet = ServiceFactory.PermissionService.GetPermissions(category, _membersGroups);
                        if (permissionSet[AppConstants.PermissionCreateTopics].IsTicked)
                        {
                            viewModel.UserCanPostTopics = true;
                            break;
                        }
                    }

                    // Now check current page
                    if (AppHelpers.CurrentPage().DocumentTypeAlias == AppConstants.DocTypeForumCategory)
                    {
                        // In a category - So pass id to create button
                        viewModel.CategoryId = CurrentPage.Id;
                    }
                }
            }
            return PartialView(PathHelper.GetThemePartialViewPath("CreateTopicButton"), viewModel);
        }


    }
    #endregion
}