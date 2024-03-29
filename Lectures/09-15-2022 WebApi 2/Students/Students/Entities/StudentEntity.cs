﻿using Students.Models;
using System.ComponentModel.DataAnnotations;

namespace Students.Entities
{
    public class StudentEntity
    {

        public StudentEntity()
        {

        }

        public StudentEntity(StudentModel model)
        {
            this.FirstName = model.FirstName;
            this.LastName = model.LastName;
        }

        [MinLength(3)]
        public string FirstName { get; set; }

        [MinLength(3)]
        public string LastName { get; set; }


        public StudentModel ToModel()
        {
            return new StudentModel()
            {
                FirstName = this.FirstName,
                LastName = this.LastName
            };
        }
    }
}
