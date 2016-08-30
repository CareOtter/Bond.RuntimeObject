namespace Bond.RuntimeObject
{
    using System.Linq.Expressions;

    /// <summary>
    /// Interface for custom factory to create objects during deserialization
    /// </summary>
    public interface IRuntimeFactory
    {
        /// <summary>
        /// Create an object
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <returns></returns>
        object CreateObject(RuntimeSchema schema, TypeDef typeDef);

        /// <summary>
        /// Create a container
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <param name="count">Initial capacity</param>
        /// <returns></returns>
        object CreateContainer(RuntimeSchema schema, TypeDef typeDef, int count);
    }

    /// <summary>
    /// Returns an expression to create an object
    /// </summary>
    /// <param name="schema"></param>
    /// <param name="typeDef"></param>
    /// <param name="arguments">Optional, type-specific argument(s). For example for containers 
    /// number of items, for IBonded&lt;T> the IBonded instance from the parser.</param>
    /// <returns></returns>
    public delegate Expression RuntimeFactory(RuntimeSchema schema, TypeDef typeDef, params Expression[] arguments);
}
