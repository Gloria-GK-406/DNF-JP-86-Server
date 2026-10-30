global using Xunit;

// Every test class here finishes by clearing the connection pool, because that is the only way to
// make the runtime really close a SQLite handle so the file can be deleted. Clearing is process
// wide, and it collides with another test opening a connection at that moment: the one opening
// gets a handle that is being disposed underneath it and fails with a disposed sqlite3, which
// says nothing about what it was checking.
//
// So the tests in this assembly run one at a time. Putting the classes in one collection would do
// the same today, since all of them clear -- but it would rely on whoever adds the next class
// remembering to say so, and forgetting brings the race back quietly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
