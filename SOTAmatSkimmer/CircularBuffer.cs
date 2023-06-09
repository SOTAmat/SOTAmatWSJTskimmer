using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOTAmatSkimmer
{
    public class CircularBuffer<T>
    {
        private T[] buffer;
        private int start;
        private int end;

        public int Count { get; private set; }
        public int Capacity { get { return buffer.Length; } }

        public CircularBuffer(int capacity)
        {
            buffer = new T[capacity];
            start = 0;
            end = 0;
            Count = 0;
        }

        public void Enqueue(T item)
        {
            buffer[end] = item;
            end = (end + 1) % Capacity;
            if (Count == Capacity)
            {
                start = (start + 1) % Capacity;
            }
            else
            {
                Count++;
            }
        }

        public T Dequeue()
        {
            if (Count > 0)
            {
                T item = buffer[start];
                start = (start + 1) % Capacity;
                Count--;
                return item;
            }
            throw new InvalidOperationException("Buffer is empty");
        }

        public T Peek()
        {
            if (Count > 0)
            {
                return buffer[start];
            }
            throw new InvalidOperationException("Buffer is empty");
        }

        public bool IsFull()
        {
            return Count == Capacity;
        }
    }
}