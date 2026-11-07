//! Buffered, deterministic event bus (C# `EventBus`). Events are delivered at phase boundaries in
//! publish order. They are outbound notifications only: handlers must not change simulation state.
use crate::error::SimError;
use std::any::{Any, TypeId};
use std::collections::HashMap;
use std::fmt::Debug;
use std::sync::Arc;

/// Limit on events published by handlers during one dispatch (guards infinite cascades).
const MAX_CASCADE: usize = 10_000;

/// Marker for notifications published by systems. Implemented for every `Any + Debug` type.
pub trait SimEvent: Any + Debug {
    fn as_any(&self) -> &dyn Any;
}

impl<T: Any + Debug> SimEvent for T {
    fn as_any(&self) -> &dyn Any {
        self
    }
}

type Handler = Box<dyn FnMut(&dyn Any, &mut EventBus)>;

#[derive(Default)]
pub struct EventBus {
    handlers: HashMap<TypeId, Vec<Option<Handler>>>,
    pending: Vec<Arc<dyn SimEvent>>,
    turn_log: Vec<Arc<dyn SimEvent>>,
}

impl EventBus {
    pub fn new() -> Self {
        Self::default()
    }

    /// Every event delivered during the last completed turn, for presentation.
    pub fn turn_log(&self) -> &[Arc<dyn SimEvent>] {
        &self.turn_log
    }

    /// The handler may publish events or subscribe more handlers while it runs.
    pub fn subscribe<E: SimEvent>(&mut self, mut handler: impl FnMut(&E, &mut EventBus) + 'static) {
        let wrapped: Handler = Box::new(move |e, bus| {
            if let Some(e) = e.downcast_ref::<E>() {
                handler(e, bus);
            }
        });
        self.handlers
            .entry(TypeId::of::<E>())
            .or_default()
            .push(Some(wrapped));
    }

    pub fn publish<E: SimEvent>(&mut self, e: E) {
        self.pending.push(Arc::new(e));
    }

    /// Delivers pending events, including ones published by handlers during delivery.
    pub fn dispatch(&mut self) -> Result<(), SimError> {
        let published = self.pending.len();
        let result = self.dispatch_inner(published);
        self.pending.clear();
        result
    }

    fn dispatch_inner(&mut self, published: usize) -> Result<(), SimError> {
        let mut i = 0;
        while i < self.pending.len() {
            if self.pending.len() - published > MAX_CASCADE {
                return Err(SimError::Invalid("Event cascade limit exceeded.".into()));
            }
            let event = Arc::clone(&self.pending[i]);
            i += 1;
            self.turn_log.push(Arc::clone(&event));
            // Deref first: `Arc<dyn SimEvent>` is itself `Any`, so calling on the Arc would mis-identify it.
            let payload: &dyn Any = (*event).as_any();
            let type_id = payload.type_id();
            self.deliver(payload, type_id);
        }
        Ok(())
    }

    fn deliver(&mut self, payload: &dyn Any, type_id: TypeId) {
        let mut h = 0;
        // Index loop: a handler may subscribe new handlers while this one runs.
        while h < self.handlers.get(&type_id).map_or(0, Vec::len) {
            self.call_handler(payload, type_id, h);
            h += 1;
        }
    }

    /// Takes the handler out of its slot so it can borrow the bus, then puts it back.
    fn call_handler(&mut self, payload: &dyn Any, type_id: TypeId, h: usize) {
        let taken = self.handlers.get_mut(&type_id).and_then(|l| l[h].take());
        let Some(mut handler) = taken else { return };
        handler(payload, self);
        if let Some(list) = self.handlers.get_mut(&type_id) {
            list[h] = Some(handler);
        }
    }

    pub(crate) fn begin_turn(&mut self) {
        self.turn_log.clear();
    }

    /// Drops undelivered events and the turn log (used after loading a save).
    pub fn reset(&mut self) {
        self.pending.clear();
        self.turn_log.clear();
    }
}
