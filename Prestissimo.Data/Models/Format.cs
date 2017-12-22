namespace Prestissimo.Data.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class Format
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(DataConstants.FormatNameMaxLength)]
        public string Name { get; set; }

        [Required]
        [MaxLength(DataConstants.FormatDescriptionMaxLength)]
        public string Description { get; set; }

        public ICollection<RecordingFormat> Recordings { get; set; } = new List<RecordingFormat>();
    }
}
