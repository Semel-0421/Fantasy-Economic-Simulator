//! Typed dense indices, matching the C# `NodeId` and `GoodId`.
use crate::enums::QualityGrade;

/// A node's dense index; `NONE` (-1) is invalid.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct NodeId(pub i32);

impl NodeId {
    pub const NONE: NodeId = NodeId(-1);

    pub fn is_valid(self) -> bool {
        self.0 >= 0
    }
}

/// Flat good id: `base_index * QualityGrade::COUNT + grade`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct GoodId(pub i32);

const GRADES: i32 = QualityGrade::COUNT as i32;

impl GoodId {
    pub const NONE: GoodId = GoodId(-1);

    pub fn is_valid(self) -> bool {
        self.0 >= 0
    }

    pub fn from(base_index: i32, grade: QualityGrade) -> GoodId {
        GoodId(base_index * GRADES + grade as i32)
    }

    pub fn base_index(self) -> i32 {
        self.0 / GRADES
    }

    /// The grade of a valid id.
    pub fn grade(self) -> QualityGrade {
        QualityGrade::from_u8((self.0 % GRADES) as u8).expect("a valid good id has a grade")
    }
}
