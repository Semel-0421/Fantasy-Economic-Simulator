//! Requested heap bytes, not process RSS; this integration binary runs one test at a time.
use std::alloc::{GlobalAlloc, Layout, System};
use std::sync::atomic::{AtomicUsize, Ordering::Relaxed};
use std::time::Instant;

pub struct Counting;
static LIVE: AtomicUsize = AtomicUsize::new(0);
static PEAK: AtomicUsize = AtomicUsize::new(0);

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let ptr = unsafe { System.alloc(layout) };
        if !ptr.is_null() {
            let live = LIVE.fetch_add(layout.size(), Relaxed) + layout.size();
            PEAK.fetch_max(live, Relaxed);
        }
        ptr
    }

    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        unsafe { System.dealloc(ptr, layout) };
        LIVE.fetch_sub(layout.size(), Relaxed);
    }
}

pub fn measure<T>(name: &str, f: impl FnOnce() -> T) -> (T, usize) {
    let before = LIVE.load(Relaxed);
    PEAK.store(before, Relaxed);
    let start = Instant::now();
    let result = f();
    let elapsed = start.elapsed();
    let retained = LIVE.load(Relaxed).saturating_sub(before);
    let peak = PEAK.load(Relaxed).saturating_sub(before);
    println!("{name}: retained_bytes={retained} peak_extra_bytes={peak} elapsed={elapsed:?}");
    (result, retained)
}
