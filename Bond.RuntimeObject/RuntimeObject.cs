namespace Bond.RuntimeObject
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// Represents an object created using a <see cref="RuntimeSchema"/> object without a corresponding .NET type.
    /// </summary>
    [Schema]
    public interface IRuntimeObject : IEnumerable<KeyValuePair<string, object>>
    {
        IDictionary<string, object> Properties { get; }
    }

    /// <summary>
    /// Represents an object created using a <see cref="RuntimeSchema"/> object without a corresponding .NET type.
    /// </summary>
    [Schema]
    public class RuntimeObject : IRuntimeObject
    {
        private readonly IDictionary<string, object> _properties = new Dictionary<string, object>();

        /// <summary>
        /// Gets a dictionary containing properties of this object.
        /// </summary>
        public IDictionary<string, object> Properties
        {
            get { return _properties; }
        }

        public void Add(string key, object value)
        {
            _properties.Add(key, value);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _properties.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _properties.GetEnumerator();
        }
    }
}
