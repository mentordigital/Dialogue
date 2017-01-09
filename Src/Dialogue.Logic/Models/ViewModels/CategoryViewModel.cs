using System.Collections.Generic;

namespace Dialogue.Logic.Models.ViewModels
{
    public class CategoryViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Image { get; set; }
    }


	public class SubCategoryViewModel
	{
		public Dictionary<Category, PermissionSet> AllPermissionSets { get; set; }
		public Category ParentCategory { get; set; }	
	}
}