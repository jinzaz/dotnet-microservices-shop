using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Mediator;

public interface IQuery<out TResult>
    : IRequest<TResult>
{
}
