using System.Linq;
using Dialogue.Logic.Interfaces.Badges;
using Dialogue.Logic.Models;
using Dialogue.Logic.Models.Attributes;
using Dialogue.Logic.Services;

namespace Badge.UserFivePost
{
	[Id("c9913ee2-b8e0-4543-8930-c723497ee65c")]
	[Name("UserFivePost")]
	[DisplayName("More than five posts")]
	[Description("This badge is awarded to users after they make their first five posts.")]
	[Image("UserVoteUpBadge.png")]
	[AwardsPoints(2)]
	public class UserFivePostBadge : IPostBadge
	{	
		public bool Rule(Member user)
		{
			var memberPosts = ServiceFactory.PostService.GetByMember(user.Id);
			return memberPosts.Count() >= 5;
		}
	}
}
