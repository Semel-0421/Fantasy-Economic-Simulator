//! Enums from the C# `Enums.cs`, with the same discriminants.

/// Declares a fieldless enum with its C# discriminants, `ALL`, `COUNT`, `name()` and `from_name`.
macro_rules! named_enum {
    ($(#[$meta:meta])* $name:ident { $($variant:ident = $value:expr),+ $(,)? }) => {
        $(#[$meta])*
        #[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
        #[repr(u8)]
        pub enum $name {
            $($variant = $value),+
        }

        impl $name {
            /// Every variant, in discriminant order.
            pub const ALL: &'static [$name] = &[$($name::$variant),+];
            /// The number of variants.
            pub const COUNT: usize = Self::ALL.len();

            /// The C# enum name.
            pub fn name(self) -> &'static str {
                match self {
                    $($name::$variant => stringify!($variant)),+
                }
            }

            /// Looks a variant up by name, ignoring ASCII case.
            pub fn from_name(name: &str) -> Option<Self> {
                Self::ALL.iter().copied().find(|v| v.name().eq_ignore_ascii_case(name))
            }

            /// The variant with this discriminant, if any.
            pub fn from_u8(value: u8) -> Option<Self> {
                Self::ALL.iter().copied().find(|v| *v as u8 == value)
            }
        }
    };
}

named_enum! {
    /// What a route crosses. River and sea routes are water.
    RouteTerrain { Road = 0, Mountain = 1, River = 2, Sea = 3 }
}

impl RouteTerrain {
    pub fn is_water(self) -> bool {
        matches!(self, RouteTerrain::River | RouteTerrain::Sea)
    }
}

named_enum! {
    /// World hierarchy level. Lower value = larger scope.
    WorldLevel { Continent = 0, Nation = 1, Region = 2, City = 3, Village = 4 }
}

// Kept: `named_enum!` declares the enum and cannot attach `#[default]` to one variant.
#[allow(clippy::derivable_impls)]
impl Default for WorldLevel {
    fn default() -> Self {
        WorldLevel::Continent
    }
}

named_enum! {
    QualityGrade { Low = 0, Mid = 1, High = 2, Best = 3 }
}

named_enum! {
    IndustryTier { Primary = 0, Secondary = 1, Tertiary = 2 }
}

named_enum! {
    /// Whether an entity is driven by the player or by its automation.
    ControlMode { Manual = 0, Automated = 1 }
}

named_enum! {
    /// Directions the player's business can grow in.
    GuidanceAxis { Trade = 0, Agriculture = 1, Industry = 2, Finance = 3, Guild = 4 }
}

named_enum! {
    /// Simulation level of detail: per-entity stepping or summarized node-level flows.
    SimDetail { Aggregate = 0, Detailed = 1 }
}

/// How often a world level's markets are stepped: due when `(turn + offset) % period == 0`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct TierCadence {
    pub period: i32,
    pub offset: i32,
}
