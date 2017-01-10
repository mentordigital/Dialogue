﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Dialogue.Logic.Application;
using Dialogue.Logic.Constants;
using Umbraco.Core.Models;

namespace Dialogue.Logic.Models
{
    public class CreateTopic : MasterModel
    {
        public CreateTopic(IPublishedContent content)
            : base(content)
        {
        }

        [Required]
        [StringLength(600)]
        [DialogueDisplayName("Topic Title")]
        public string TopicName { get; set; }

        [UIHint(AppConstants.EditorType), AllowHtml]
        [StringLength(6000)]
        public string TopicContent { get; set; }

        public bool IsSticky { get; set; }

        public bool IsLocked { get; set; }

        [Required]
        [DialogueDisplayName("Category")]
        public int Category { get; set; }

        public IEnumerable<Category> Categories { get; set; }

        public List<PollAnswer> PollAnswers { get; set; }

        [DialogueDisplayName("Subscribe To Topic")]
        public bool SubscribeToTopic { get; set; }

        public Member LoggedOnUser { get; set; }

		public int PageId { get; set; }
	}
}