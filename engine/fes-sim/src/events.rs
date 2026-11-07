//! Buffered, deterministic event bus (C# `EventBus`). Events are delivered at phase boundaries in
//! publish order by `SimContext::dispatch_events`. As in C#, handlers receive the whole context:
//! they may change world state, publish more events or subscribe more handlers.
use crate::context::SimContext;
use crate::error::SimError;
use std::any::{Any, TypeId};
use std::collections::HashMap;
use std::fmt::Debug;
use std::sync::Arc;

/// Limit on events published by handlers during one dispatch (guards infinite cascades).
const MAX_CASCADE: usize = 10_000;

/// Marker for notifications published by systems. Implemented for every `Any + Debug + Send + Sync`
/// type.
pub trait SimEvent: Any + Debug + Send + Sync {
    fn as_any(&self) -> &dyn Any;
    /// The concrete event type's name (for the plain-data turn report).
    fn type_name(&self) -> &'static str;
}

impl<T: Any + Debug + Send + Sync> SimEvent for T {
    fn as_any(&self) -> &dyn Any {
        self
    }

    fn type_name(&self) -> &'static str {
        std::any::type_name::<T>()
    }
}

type Handler = Box<dyn FnMut(&dyn Any, &mut SimContext) -> Result<(), SimError> + Send>;

#[derive(Default)]
pub struct EventBus {
    handlers: HashMap<TypeId, Vec<Handler>>,
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

    /// The handler may change the world, publish events or subscribe more handlers while it runs.
    pub fn subscribe<E: SimEvent>(
        &mut self,
        mut handler: impl FnMut(&E, &mut SimContext) -> Result<(), SimError> + Send + 'static,
    ) {
        let wrapped: Handler = Box::new(move |e, ctx| match e.downcast_ref::<E>() {
            Some(e) => handler(e, ctx),
            None => Ok(()),
        });
        self.handlers
            .entry(TypeId::of::<E>())
            .or_default()
            .push(wrapped);
    }

    pub fn publish<E: SimEvent>(&mut self, e: E) {
        self.pending.push(Arc::new(e));
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

impl SimContext {
    /// Delivers pending events, including ones published by handlers during delivery (C#
    /// `EventBus.Dispatch`). Pending events are cleared even when a handler fails.
    pub fn dispatch_events(&mut self) -> Result<(), SimError> {
        let published = self.events.pending.len();
        let result = self.dispatch_pending(published);
        self.events.pending.clear();
        result
    }

    fn dispatch_pending(&mut self, published: usize) -> Result<(), SimError> {
        let mut i = 0;
        while i < self.events.pending.len() {
            if self.events.pending.len() - published > MAX_CASCADE {
                return Err(SimError::Invalid("Event cascade limit exceeded.".into()));
            }
            let event = Arc::clone(&self.events.pending[i]);
            i += 1;
            self.events.turn_log.push(Arc::clone(&event));
            // Deref first: `Arc<dyn SimEvent>` is itself `Any`, so calling on the Arc would mis-identify it.
            let payload: &dyn Any = (*event).as_any();
            self.deliver(payload, payload.type_id())?;
        }
        Ok(())
    }

    /// Takes the type's handler list out of the bus so handlers can borrow the context, then puts
    /// it back. Handlers subscribed for this type meanwhile are appended after each call, so the
    /// index loop reaches them for this same event, as C#'s does.
    fn deliver(&mut self, payload: &dyn Any, type_id: TypeId) -> Result<(), SimError> {
        let Some(mut list) = self.events.handlers.remove(&type_id) else {
            return Ok(());
        };
        let mut result = Ok(());
        let mut h = 0;
        while h < list.len() {
            result = (list[h])(payload, self);
            if let Some(added) = self.events.handlers.remove(&type_id) {
                list.extend(added);
            }
            if result.is_err() {
                break;
            }
            h += 1;
        }
        self.events.handlers.insert(type_id, list);
        result
    }
}
